using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

// Your own bookkeeping on your own recipes: favorites, private notes, a cooking log, and collections.
// Every query is scoped to the signed-in owner, so none of it can touch anyone else's recipes.
public static class PersonalEndpoints
{
    public static void MapPersonalEndpoints(this IEndpointRouteBuilder app)
    {
        var recipe = app.MapGroup("/api/recipes/{id:int}").RequireAuthorization();

        recipe.MapPut("/favorite", async (int id, FavoriteRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var r = await Mine(db, user, id);
            if (r is null) return Results.NotFound();
            r.IsFavorite = request.IsFavorite;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        recipe.MapPut("/notes", async (int id, NotesRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            if (notes?.Length > RecipeLimits.MaxNotesLength)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Notes"] = [$"Notes must be {RecipeLimits.MaxNotesLength} characters or fewer."] });
            var r = await Mine(db, user, id);
            if (r is null) return Results.NotFound();
            r.Notes = notes;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        recipe.MapPost("/cooked", async (int id, CookedRequest? request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var day = request?.CookedOn ?? today;
            // A day or so ahead allows for time zones; anything further is a typo
            if (day > today.AddDays(1) || day < today.AddYears(-20))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["CookedOn"] = ["That date doesn't look right."] });
            if (await Mine(db, user, id) is null) return Results.NotFound();
            if (await db.CookLog.CountAsync(c => c.RecipeId == id) >= AccountLimits.MaxCookLogEntries)
                return AccountLimits.TooManyCookLogEntries();

            var entry = new CookLogEntry { RecipeId = id, CookedOn = day };
            db.CookLog.Add(entry);
            await db.SaveChangesAsync();
            return Results.Created($"/api/recipes/{id}/cooked/{entry.Id}", new CookLogResponse(entry.Id, entry.CookedOn));
        });

        recipe.MapDelete("/cooked/{entryId:int}", async (int id, int entryId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var entry = await db.CookLog.FirstOrDefaultAsync(c => c.Id == entryId && c.RecipeId == id
                && db.Recipes.Any(r => r.Id == id && r.OwnerId == userId));
            if (entry is null) return Results.NotFound();
            db.CookLog.Remove(entry);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        recipe.MapPut("/collections", async (int id, List<int> collectionIds, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var r = await db.Recipes.Include(x => x.Collections).FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == userId);
            if (r is null) return Results.NotFound();
            var wanted = collectionIds.Distinct().ToList();
            var collections = await db.RecipeCollections.Where(c => wanted.Contains(c.Id) && c.OwnerId == userId).ToListAsync();
            if (collections.Count != wanted.Count)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["CollectionIds"] = ["One or more collections don't exist."] });
            r.Collections = collections;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---------- Collections ----------

        var collections = app.MapGroup("/api/collections").RequireAuthorization();

        collections.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var list = await db.RecipeCollections.AsNoTracking()
                .Where(c => c.OwnerId == userId)
                .OrderBy(c => c.Name)
                .Select(c => new CollectionResponse(c.Id, c.Name, c.Recipes.Count))
                .ToListAsync();
            return Results.Ok(list);
        });

        collections.MapPost("/", async (CollectionRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            if (ValidateName(request.Name) is { } errors) return Results.ValidationProblem(errors);
            var name = request.Name.Trim();
            if (await db.RecipeCollections.CountAsync(c => c.OwnerId == userId) >= RecipeLimits.MaxCollectionsPerUser)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Name"] = [$"You can have up to {RecipeLimits.MaxCollectionsPerUser} collections."] });
            if (await db.RecipeCollections.AnyAsync(c => c.OwnerId == userId && c.Name.ToLower() == name.ToLower()))
                return Results.Conflict(new { message = "You already have a collection with that name." });

            var collection = new RecipeCollection { Name = name, OwnerId = userId };
            db.RecipeCollections.Add(collection);
            await db.SaveChangesAsync();
            return Results.Created($"/api/collections/{collection.Id}", new CollectionResponse(collection.Id, collection.Name, 0));
        });

        collections.MapPut("/{id:int}", async (int id, CollectionRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            if (ValidateName(request.Name) is { } errors) return Results.ValidationProblem(errors);
            var name = request.Name.Trim();
            var collection = await db.RecipeCollections.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == userId);
            if (collection is null) return Results.NotFound();
            if (await db.RecipeCollections.AnyAsync(c => c.OwnerId == userId && c.Id != id && c.Name.ToLower() == name.ToLower()))
                return Results.Conflict(new { message = "You already have a collection with that name." });
            collection.Name = name;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        collections.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var collection = await db.RecipeCollections.Include(c => c.Recipes).FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == userId);
            if (collection is null) return Results.NotFound();
            db.RecipeCollections.Remove(collection); // its recipes stay; only the grouping goes
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static Task<Recipe?> Mine(AppDbContext db, ClaimsPrincipal user, int id)
    {
        var userId = user.UserId();
        return db.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
    }

    private static Dictionary<string, string[]>? ValidateName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > RecipeLimits.MaxCollectionNameLength
            ? new() { ["Name"] = [$"Name is required ({RecipeLimits.MaxCollectionNameLength} characters max)."] }
            : null;
}
