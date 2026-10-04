using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class MealPlanEndpoints
{
    public static void MapMealPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mealplans");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var plans = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe)
                .OrderByDescending(p => p.WeekStart)
                .ToListAsync();
            return Results.Ok(plans.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe)
                .FirstOrDefaultAsync(p => p.Id == id);
            return plan is null ? Results.NotFound() : Results.Ok(ToResponse(plan));
        });

        group.MapPost("/", async (MealPlanRequest request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Name"] = ["Name is required (100 characters max)."] });

            var plan = new MealPlan { Name = request.Name.Trim(), WeekStart = request.WeekStart };
            db.MealPlans.Add(plan);
            await db.SaveChangesAsync();
            return Results.Created($"/api/mealplans/{plan.Id}", ToResponse(plan));
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var plan = await db.MealPlans.FindAsync(id);
            if (plan is null) return Results.NotFound();

            db.MealPlans.Remove(plan);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Replaces the plan's full list of entries
        group.MapPut("/{id:int}/entries", async (int id, List<MealPlanEntryRequest> entries, AppDbContext db) =>
        {
            var plan = await db.MealPlans.Include(p => p.Entries).FirstOrDefaultAsync(p => p.Id == id);
            if (plan is null) return Results.NotFound();

            var errors = new Dictionary<string, string[]>();
            var weekEnd = plan.WeekStart.AddDays(6);
            if (entries.Any(e => e.Day < plan.WeekStart || e.Day > weekEnd))
                errors["Day"] = [$"Days must fall between {plan.WeekStart} and {weekEnd}."];
            if (entries.Any(e => e.Servings < 1))
                errors["Servings"] = ["Servings must be at least 1."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
            var found = await db.Recipes.CountAsync(r => recipeIds.Contains(r.Id));
            if (found != recipeIds.Count)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["RecipeId"] = ["One or more recipes do not exist."] });

            plan.Entries.Clear();
            foreach (var e in entries)
                plan.Entries.Add(new MealPlanEntry { Day = e.Day, RecipeId = e.RecipeId, Servings = e.Servings });

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapGet("/{id:int}/shopping-list", async (int id, AppDbContext db) =>
        {
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries)
                    .ThenInclude(e => e.Recipe)
                    .ThenInclude(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id);
            if (plan is null) return Results.NotFound();

            var items = plan.Entries
                .SelectMany(e => e.Recipe.Ingredients.Select(ri => new
                {
                    ri.Ingredient.Name,
                    ri.Ingredient.StoreSection,
                    Unit = ri.Unit.ToLowerInvariant(),
                    // Scale to planned servings: a 4-serving recipe planned for 8 doubles everything
                    Quantity = ri.Quantity * e.Servings / Math.Max(e.Recipe.Servings, 1)
                }))
                .GroupBy(x => new { x.Name, x.Unit, x.StoreSection })
                .Select(g => new ShoppingListItem(
                    g.Key.Name, Math.Round(g.Sum(x => x.Quantity), 2), g.Key.Unit, g.Key.StoreSection))
                .OrderBy(i => i.StoreSection ?? "~")
                .ThenBy(i => i.Ingredient)
                .ToList();

            return Results.Ok(new ShoppingListResponse(plan.Id, plan.Name, items));
        });
    }

    private static MealPlanResponse ToResponse(MealPlan p) =>
        new(p.Id, p.Name, p.WeekStart,
            p.Entries.OrderBy(e => e.Day)
                .Select(e => new MealPlanEntryResponse(e.Day, e.RecipeId, e.Recipe?.Title ?? "", e.Servings))
                .ToList());
}