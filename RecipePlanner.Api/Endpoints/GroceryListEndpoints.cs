using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

// Each user has one running shopping list. Items can come from recipes, meal plans,
// the fridge planner, or be typed in. Same item + same unit is combined into one line.
public static class GroceryListEndpoints
{
    public const int MaxItems = 300;
    public const int MaxBatch = 100;
    private const int MaxNameLength = 100;
    private const int MaxUnitLength = 20;
    private const int MaxSourcesLength = 200;
    private const decimal MaxQuantity = 10_000m;

    public static void MapGroceryListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopping-list").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            Results.Ok(await List(db, user.UserId())));

        // Typed-in items, the fridge planner's "still need" list, or an AI recipe's missing ingredients
        group.MapPost("/items", async (List<GroceryAddRequest> items, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (items.Count is 0 or > MaxBatch)
                return Problem(new() { ["Items"] = [$"Send between 1 and {MaxBatch} items."] });

            var errors = new Dictionary<string, string[]>();
            foreach (var i in items)
            {
                if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length > MaxNameLength)
                    errors["Name"] = [$"Each item needs a name of {MaxNameLength} characters or fewer."];
                if (i.Quantity is <= 0 or > MaxQuantity)
                    errors["Quantity"] = ["Quantities must be greater than zero and reasonable."];
                if (i.Unit?.Trim().Length > MaxUnitLength)
                    errors["Unit"] = [$"Units must be {MaxUnitLength} characters or fewer."];
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // Link names to known ingredients so they land in the right store section
            var names = items.Select(i => i.Name.Trim().ToLower()).Distinct().ToList();
            var known = await db.Ingredients.Where(x => names.Contains(x.Name.ToLower())).ToListAsync();
            var lines = items.Select(i =>
            {
                var match = known.FirstOrDefault(k => k.Name.Equals(i.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                return new Line(match?.Id, match?.Name ?? i.Name.Trim(), match?.StoreSection, i.Quantity, i.Unit, i.Source);
            }).ToList();

            return await AddAndList(db, user.UserId(), lines);
        });

        // Everything a recipe needs, scaled to the servings you're making
        // Pantry staples are left off unless includePantry=true
        group.MapPost("/from-recipe/{id:int}", async (int id, GroceryFromRecipeRequest? request, bool? includePantry, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.AsNoTracking()
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            if (recipe is null) return Results.NotFound();

            var servings = request?.Servings ?? recipe.Servings;
            if (servings is < 1 or > 100)
                return Problem(new() { ["Servings"] = ["Servings must be between 1 and 100."] });

            var scale = (decimal)servings / Math.Max(recipe.Servings, 1);
            var lines = recipe.Ingredients.Select(ri => new Line(
                ri.IngredientId, ri.Ingredient.Name, ri.Ingredient.StoreSection,
                Math.Round(ri.Quantity * scale, 2), ri.Unit, recipe.Title)).ToList();
            return await AddWithoutPantry(db, userId, lines, includePantry == true);
        });

        // Every recipe in a meal plan, each scaled to its planned servings
        group.MapPost("/from-meal-plan/{id:int}", async (int id, bool? includePantry, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe)
                    .ThenInclude(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);
            if (plan is null) return Results.NotFound();

            var lines = plan.Entries.SelectMany(e => e.Recipe.Ingredients.Select(ri => new Line(
                ri.IngredientId, ri.Ingredient.Name, ri.Ingredient.StoreSection,
                Math.Round(ri.Quantity * e.Servings / Math.Max(e.Recipe.Servings, 1), 2), ri.Unit, plan.Name))).ToList();
            return await AddWithoutPantry(db, userId, lines, includePantry == true);
        });

        group.MapPatch("/items/{id:int}", async (int id, GroceryUpdateRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var item = await db.GroceryItems.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == user.UserId());
            if (item is null) return Results.NotFound();
            if (request.Quantity is <= 0 or > MaxQuantity)
                return Problem(new() { ["Quantity"] = ["Quantities must be greater than zero and reasonable."] });

            if (request.Checked is { } isChecked) item.Checked = isChecked;
            if (request.Quantity is { } quantity) item.Quantity = quantity;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/items/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var item = await db.GroceryItems.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == user.UserId());
            if (item is null) return Results.NotFound();
            db.GroceryItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ?checkedOnly=true clears what's already in the cart; otherwise empties the whole list
        group.MapDelete("/", async (bool? checkedOnly, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var doomed = await db.GroceryItems
                .Where(g => g.OwnerId == userId && (checkedOnly != true || g.Checked))
                .ToListAsync();
            db.GroceryItems.RemoveRange(doomed);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static IResult Problem(Dictionary<string, string[]> errors) => Results.ValidationProblem(errors);

    private record Line(int? IngredientId, string Name, string? StoreSection, decimal? Quantity, string? Unit, string? Source);

    // Same unit spelled different ways should still combine: "Tbsp", "tablespoons" -> "tbsp"
    public static string? NormalizeUnit(string? unit)
    {
        var u = unit?.Trim().ToLowerInvariant().TrimEnd('.');
        if (string.IsNullOrEmpty(u)) return null;
        return u switch
        {
            "tablespoon" or "tablespoons" or "tbsps" or "tbs" => "tbsp",
            "teaspoon" or "teaspoons" or "tsps" => "tsp",
            "cups" => "cup",
            "pound" or "pounds" or "lbs" => "lb",
            "ounce" or "ounces" => "oz",
            "gram" or "grams" => "g",
            "kilogram" or "kilograms" => "kg",
            "milliliter" or "milliliters" => "ml",
            "liter" or "liters" => "l",
            "cloves" => "clove",
            "cans" => "can",
            "each" or "ea" or "whole" or "pieces" or "piece" => "whole",
            _ => u
        };
    }

    // Leaves off what's in the pantry, and says which ones, so nothing disappears without a word
    private static async Task<IResult> AddWithoutPantry(AppDbContext db, string userId, List<Line> lines, bool includePantry)
    {
        var skipped = new List<string>();
        if (!includePantry)
        {
            var pantry = await KitchenEndpoints.PantryKeys(db, userId);
            skipped = lines.Where(l => pantry.Contains(Kitchen.KitchenRules.Key(l.Name)))
                .Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            lines = lines.Where(l => !pantry.Contains(Kitchen.KitchenRules.Key(l.Name))).ToList();
        }
        var result = await AddAndList(db, userId, lines);
        return result is Microsoft.AspNetCore.Http.HttpResults.Ok<List<GroceryItemResponse>> ok
            ? Results.Ok(new GroceryAddResult(ok.Value!, skipped))
            : result;
    }

    private static async Task<IResult> AddAndList(AppDbContext db, string userId, List<Line> lines)
    {
        var existing = await db.GroceryItems.Where(g => g.OwnerId == userId).ToListAsync();

        foreach (var line in lines)
        {
            var unit = NormalizeUnit(line.Unit);
            // Only unchecked lines are candidates: a checked line is already in the cart,
            // so a new need starts a fresh line.
            var sameItem = existing.Where(g => !g.Checked &&
                (line.IngredientId is { } ingId
                    ? g.IngredientId == ingId
                    : g.IngredientId == null && g.Name.Equals(line.Name, StringComparison.OrdinalIgnoreCase))).ToList();

            // Same thing in the same unit: add the amounts together
            var match = sameItem.FirstOrDefault(g => g.Unit == unit && (g.Quantity is null) == (line.Quantity is null));
            // No amount given ("Lemon" from the fridge planner): it's already on the list, so just note why
            match ??= line.Quantity is null && unit is null ? sameItem.FirstOrDefault() : null;
            // An amount for a line that had none: fill it in instead of adding a second line
            if (match is null && line.Quantity is not null &&
                sameItem.FirstOrDefault(g => g.Quantity is null && g.Unit is null) is { } bare)
            {
                bare.Quantity = line.Quantity;
                bare.Unit = unit;
                bare.Sources = AddSource(bare.Sources, line.Source);
                continue;
            }

            if (match is not null)
            {
                if (line.Quantity is { } q)
                    match.Quantity = Math.Min(MaxQuantity, (match.Quantity ?? 0) + q);
                match.Sources = AddSource(match.Sources, line.Source);
                continue;
            }

            if (existing.Count >= MaxItems)
                return Problem(new() { ["Items"] = [$"Your list can hold up to {MaxItems} items. Clear some first."] });

            var item = new GroceryItem
            {
                OwnerId = userId,
                IngredientId = line.IngredientId,
                Name = line.Name,
                StoreSection = line.StoreSection,
                Quantity = line.Quantity,
                Unit = unit,
                Sources = AddSource(null, line.Source),
                AddedAt = DateTimeOffset.UtcNow
            };
            db.GroceryItems.Add(item);
            existing.Add(item);
        }

        await db.SaveChangesAsync();
        return Results.Ok(await List(db, userId));
    }

    private static string? AddSource(string? sources, string? source)
    {
        source = source?.Trim();
        if (string.IsNullOrEmpty(source)) return sources;
        var parts = (sources ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!parts.Contains(source, StringComparer.OrdinalIgnoreCase)) parts.Add(source);
        var joined = string.Join(", ", parts);
        return joined.Length <= MaxSourcesLength ? joined : joined[..(MaxSourcesLength - 1)] + "…";
    }

    private static async Task<List<GroceryItemResponse>> List(AppDbContext db, string userId) =>
        (await db.GroceryItems.AsNoTracking().Where(g => g.OwnerId == userId).ToListAsync())
            .OrderBy(g => g.Checked)
            .ThenBy(g => g.Name)
            .Select(g => new GroceryItemResponse(g.Id, g.Name, g.Quantity, g.Unit, g.StoreSection, g.Checked, g.Sources))
            .ToList();
}
