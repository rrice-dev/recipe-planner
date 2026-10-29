using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Kitchen;

namespace RecipePlanner.Api.Endpoints;

// Your kitchen: pantry staples you always have, and leftovers waiting to be eaten
public static class KitchenEndpoints
{
    public static void MapKitchenEndpoints(this IEndpointRouteBuilder app)
    {
        var pantry = app.MapGroup("/api/pantry").RequireAuthorization();

        pantry.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            Results.Ok(await PantryList(db, user.UserId())));

        // Several at once, so "add common staples" is one request. Ones you already have are ignored.
        pantry.MapPost("/", async (PantryAddRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var names = (request.Names ?? []).Select(n => n?.Trim() ?? "").ToList();
            if (names.Count is 0 or > 50 || names.Any(n => n.Length is 0 or > KitchenRules.MaxNameLength))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Names"] = [$"Send 1 to 50 names, each {KitchenRules.MaxNameLength} characters or fewer."] });

            var userId = user.UserId();
            var existing = (await db.PantryItems.Where(p => p.OwnerId == userId).Select(p => p.NameKey).ToListAsync()).ToHashSet();
            var toAdd = names.DistinctBy(KitchenRules.Key).Where(n => !existing.Contains(KitchenRules.Key(n))).ToList();
            if (existing.Count + toAdd.Count > KitchenRules.MaxPantryItems)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Names"] = [$"Your pantry can hold up to {KitchenRules.MaxPantryItems} staples."] });

            // Use a known ingredient's spelling when there is one, so the list reads consistently
            var keys = toAdd.Select(KitchenRules.Key).ToList();
            var known = await db.Ingredients.VisibleTo(userId).Where(i => keys.Contains(i.Name.ToLower())).Select(i => i.Name).ToListAsync();
            foreach (var name in toAdd)
            {
                var display = known.FirstOrDefault(k => KitchenRules.Key(k) == KitchenRules.Key(name)) ?? name;
                db.PantryItems.Add(new PantryItem { OwnerId = userId, Name = display, NameKey = KitchenRules.Key(name) });
            }
            await db.SaveChangesAsync();
            return Results.Ok(await PantryList(db, userId));
        });

        pantry.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var item = await db.PantryItems.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == user.UserId());
            if (item is null) return Results.NotFound();
            db.PantryItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        var leftovers = app.MapGroup("/api/leftovers").RequireAuthorization();

        // Soonest to eat first
        leftovers.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            Results.Ok(await LeftoverList(db, user.UserId())));

        leftovers.MapPost("/", async (LeftoverRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var errors = new Dictionary<string, string[]>();
            if (request.Portions is < 1 or > KitchenRules.MaxPortions)
                errors["Portions"] = [$"Portions must be between 1 and {KitchenRules.MaxPortions}."];
            if (!KitchenRules.IsLocation(request.Location))
                errors["Location"] = ["Choose the fridge or the freezer."];
            var madeOn = request.MadeOn ?? today;
            // A day of slack each way for time zones. Freezer food can be months old; fridge food can't.
            var oldest = today.AddDays(request.Location == KitchenRules.Freezer ? -365 : -KitchenRules.FridgeDays);
            if (madeOn > today.AddDays(1) || madeOn < oldest.AddDays(-1))
                errors["MadeOn"] = ["Check the day it was made."];

            string? name = request.Name?.Trim();
            if (request.RecipeId is { } recipeId)
            {
                var title = await db.Recipes.Where(r => r.Id == recipeId && r.OwnerId == userId).Select(r => r.Title).FirstOrDefaultAsync();
                if (title is null) return Results.NotFound();
                if (string.IsNullOrEmpty(name)) name = title;
            }
            if (string.IsNullOrEmpty(name) || name.Length > KitchenRules.MaxNameLength)
                errors["Name"] = [$"Give it a name of {KitchenRules.MaxNameLength} characters or fewer."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (await db.Leftovers.CountAsync(l => l.OwnerId == userId) >= KitchenRules.MaxLeftovers)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Leftovers"] = [$"You can track up to {KitchenRules.MaxLeftovers} leftovers. Clear some first."] });

            var leftover = new Leftover
            {
                OwnerId = userId, Name = name!, RecipeId = request.RecipeId, Portions = request.Portions,
                Location = request.Location, MadeOn = madeOn, UseBy = KitchenRules.UseBy(request.Location, madeOn)
            };
            db.Leftovers.Add(leftover);
            await db.SaveChangesAsync();
            return Results.Created($"/api/leftovers/{leftover.Id}", ToResponse(leftover));
        });

        // Change the portion count, or move it: into the freezer before its fridge date, or out to thaw
        leftovers.MapPatch("/{id:int}", async (int id, LeftoverUpdateRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var leftover = await db.Leftovers.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == user.UserId());
            if (leftover is null) return Results.NotFound();

            if (request.Portions is { } portions)
            {
                if (portions is < 1 or > KitchenRules.MaxPortions)
                    return Problem("Portions", $"Portions must be between 1 and {KitchenRules.MaxPortions}.");
                leftover.Portions = portions;
            }

            if (request.Location is { } location && location != leftover.Location)
            {
                if (!KitchenRules.IsLocation(location)) return Problem("Location", "Choose the fridge or the freezer.");
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var on = request.On ?? today;
                if (Math.Abs(on.DayNumber - today.DayNumber) > 1) return Problem("On", "Check today's date.");

                // Freezing doesn't make food safe again, it only pauses it. Past its fridge date, it's too late.
                if (location == KitchenRules.Freezer && on > leftover.UseBy)
                    return Problem("Location", "This is past its fridge date, so freezing it won't make it safe. It's best thrown out.");

                leftover.Location = location;
                leftover.UseBy = KitchenRules.UseBy(location, on);
            }

            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(leftover));
        });

        // Ate some. When none are left, it's removed.
        leftovers.MapPost("/{id:int}/eat", async (int id, LeftoverEatRequest? request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var leftover = await db.Leftovers.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == user.UserId());
            if (leftover is null) return Results.NotFound();
            var eaten = request?.Portions ?? 1;
            if (eaten is < 1 or > KitchenRules.MaxPortions) return Problem("Portions", "Eat at least one portion.");

            leftover.Portions -= eaten;
            if (leftover.Portions <= 0)
            {
                db.Leftovers.Remove(leftover);
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(leftover));
        });

        leftovers.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var leftover = await db.Leftovers.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == user.UserId());
            if (leftover is null) return Results.NotFound();
            db.Leftovers.Remove(leftover);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    // The keys of everything in a user's pantry, for leaving staples off shopping lists
    public static async Task<HashSet<string>> PantryKeys(AppDbContext db, string userId) =>
        (await db.PantryItems.Where(p => p.OwnerId == userId).Select(p => p.NameKey).ToListAsync()).ToHashSet();

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static async Task<List<PantryItemResponse>> PantryList(AppDbContext db, string userId) =>
        (await db.PantryItems.AsNoTracking().Where(p => p.OwnerId == userId).ToListAsync())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PantryItemResponse(p.Id, p.Name)).ToList();

    private static async Task<List<LeftoverResponse>> LeftoverList(AppDbContext db, string userId) =>
        (await db.Leftovers.AsNoTracking().Where(l => l.OwnerId == userId).ToListAsync())
            .OrderBy(l => l.UseBy).ThenBy(l => l.Name)
            .Select(ToResponse).ToList();

    private static LeftoverResponse ToResponse(Leftover l) =>
        new(l.Id, l.Name, l.RecipeId, l.Portions, l.Location, l.MadeOn, l.UseBy);
}
