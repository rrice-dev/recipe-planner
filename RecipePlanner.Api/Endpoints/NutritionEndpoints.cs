using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using RecipePlanner.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Email;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Nutrition;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

public static class NutritionEndpoints
{
    public static void MapNutritionEndpoints(this IEndpointRouteBuilder app)
    {
        // Which optional features are set up, so the app can hide what isn't
        app.MapGet("/api/features", (IOptions<EmailOptions> email, NutritionService nutrition) =>
            Results.Ok(new FeaturesResponse(email.Value.IsConfigured, nutrition.IsAvailable)));

        app.MapGet("/api/recipes/{id:int}/nutrition", async (int id, ClaimsPrincipal user, AppDbContext db, NutritionService nutrition, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.AsNoTracking().Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId, ct);
            if (recipe is null) return Results.NotFound();
            return Results.Ok(await Calculate(nutrition, recipe.Servings,
                recipe.Ingredients.Select(i => (i.Ingredient.Name, i.Quantity, i.Unit)).ToList(), ct));
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.NutritionPolicy);

        // Calories per serving for every recipe in your book, for filtering the list.
        // New ingredient names are looked up a batch at a time; Pending says to call again for the rest.
        app.MapGet("/api/recipes/calories", async (ClaimsPrincipal user, AppDbContext db, NutritionService nutrition, CancellationToken ct) =>
        {
            if (!nutrition.IsAvailable) return Results.Ok(new RecipeCaloriesResponse(false, false, []));
            var userId = user.UserId();
            var recipes = await db.Recipes.AsNoTracking().Where(r => r.OwnerId == userId)
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .AsSplitQuery()
                .ToListAsync(ct);

            var (facts, skipped) = await nutrition.FactsWithStatusAsync(recipes.SelectMany(r => r.Ingredients.Select(i => i.Ingredient.Name)), ct);
            var list = recipes.Select(r =>
            {
                var (totals, lines) = NutritionCalculator.Calculate(r.Ingredients.Select(i =>
                    (i.Ingredient.Name, i.Quantity, i.Unit, facts.GetValueOrDefault(i.Ingredient.Name.Trim().ToLowerInvariant()))));
                var counted = lines.Count(l => l.Counted);
                return new RecipeCalories(r.Id, counted == 0 ? null : PerServing(totals, r.Servings).Calories, counted > 0 && counted == lines.Count);
            }).ToList();
            return Results.Ok(new RecipeCaloriesResponse(true, skipped > 0, list));
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.NutritionPolicy);

        app.MapGet("/api/library/{slug}/nutrition", async (string slug, RecipeLibrary library, NutritionService nutrition, CancellationToken ct) =>
        {
            var r = library.Find(slug);
            if (r is null) return Results.NotFound();
            return Results.Ok(await Calculate(nutrition, r.Servings, r.Ingredients.Select(i => (i.Name, i.Quantity, i.Unit)).ToList(), ct));
        })
        .RequireRateLimiting(RateLimiting.NutritionPolicy);

        // A planned week, day by day: one serving of each meal, with the user's daily targets to compare
        app.MapGet("/api/mealplans/{id:int}/nutrition", async (int id, ClaimsPrincipal user, AppDbContext db,
            UserManager<AppUser> users, NutritionService nutrition, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId, ct);
            if (plan is null) return Results.NotFound();
            var me = await users.GetUserAsync(user);
            var targets = me is null ? new NutritionTargets(null, null, null, null) : TargetsOf(me);
            if (!nutrition.IsAvailable) return Results.Ok(new PlanNutritionResponse(false, [], null, targets, false));

            // Every ingredient in the week in one lookup, so cached names cost nothing
            var recipes = plan.Entries.Select(e => e.Recipe).DistinctBy(r => r.Id).ToList();
            var facts = await nutrition.FactsForAsync(recipes.SelectMany(r => r.Ingredients.Select(i => i.Ingredient.Name)), ct);
            var perRecipe = recipes.ToDictionary(r => r.Id, r =>
            {
                var (totals, lines) = NutritionCalculator.Calculate(r.Ingredients.Select(i =>
                    (i.Ingredient.Name, i.Quantity, i.Unit, facts.GetValueOrDefault(i.Ingredient.Name.Trim().ToLowerInvariant()))));
                var counted = lines.Count(l => l.Counted);
                return (PerServing: counted == 0 ? null : PerServing(totals, r.Servings), Complete: counted == lines.Count);
            });

            var days = plan.Entries.GroupBy(e => e.Day).OrderBy(g => g.Key).Select(g =>
            {
                var meals = g.OrderBy(e => e.Id).Select(e => new PlanMealNutrition(e.RecipeId, e.Recipe.Title,
                    perRecipe[e.RecipeId].PerServing, perRecipe[e.RecipeId].Complete)).ToList();
                return new PlanDayNutrition(g.Key, meals, Sum(meals.Select(m => m.PerServing).OfType<NutrientsResponse>().ToList()));
            }).ToList();

            var average = days.Count == 0 ? null : Divide(Sum(days.Select(d => d.Total).ToList()), days.Count);
            return Results.Ok(new PlanNutritionResponse(true, days, average, targets, perRecipe.Values.All(v => v.Complete)));
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.NutritionPolicy);

        app.MapGet("/api/settings/nutrition-targets", async (ClaimsPrincipal user, UserManager<AppUser> users) =>
            await users.GetUserAsync(user) is { } me ? Results.Ok(TargetsOf(me)) : Results.Unauthorized())
            .RequireAuthorization();

        app.MapPut("/api/settings/nutrition-targets", async (NutritionTargets request, ClaimsPrincipal user, UserManager<AppUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.Calories is < UserSettings.MinCalorieTarget or > UserSettings.MaxCalorieTarget)
                errors["Calories"] = [$"Calories must be between {UserSettings.MinCalorieTarget} and {UserSettings.MaxCalorieTarget}, or empty."];
            foreach (var (name, value) in new[] { ("Protein", request.Protein), ("Carbs", request.Carbs), ("Fat", request.Fat) })
                if (value is < 0 or > UserSettings.MaxGramTarget)
                    errors[name] = [$"{name} must be between 0 and {UserSettings.MaxGramTarget} grams, or empty."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var me = await users.GetUserAsync(user);
            if (me is null) return Results.Unauthorized();
            (me.CalorieTarget, me.ProteinTarget, me.CarbsTarget, me.FatTarget) = (request.Calories, request.Protein, request.Carbs, request.Fat);
            await users.UpdateAsync(me);
            return Results.Ok(TargetsOf(me));
        })
        .RequireAuthorization();
    }

    private static NutritionTargets TargetsOf(AppUser u) => new(u.CalorieTarget, u.ProteinTarget, u.CarbsTarget, u.FatTarget);

    private static NutrientsResponse PerServing(NutritionCalculator.Totals t, int servings)
    {
        var per = Math.Max(servings, 1);
        return new NutrientsResponse((int)Math.Round(t.Calories / per), Math.Round(t.Protein / per, 1), Math.Round(t.Carbs / per, 1),
            Math.Round(t.Fat / per, 1), Math.Round(t.Fiber / per, 1), Math.Round(t.Sugar / per, 1), (int)Math.Round(t.SodiumMg / per));
    }

    private static NutrientsResponse Sum(List<NutrientsResponse> all) => new(
        all.Sum(n => n.Calories), Math.Round(all.Sum(n => n.Protein), 1), Math.Round(all.Sum(n => n.Carbs), 1),
        Math.Round(all.Sum(n => n.Fat), 1), Math.Round(all.Sum(n => n.Fiber), 1), Math.Round(all.Sum(n => n.Sugar), 1), all.Sum(n => n.SodiumMg));

    private static NutrientsResponse Divide(NutrientsResponse n, int by) => new(
        (int)Math.Round((double)n.Calories / by), Math.Round(n.Protein / by, 1), Math.Round(n.Carbs / by, 1),
        Math.Round(n.Fat / by, 1), Math.Round(n.Fiber / by, 1), Math.Round(n.Sugar / by, 1), (int)Math.Round((double)n.SodiumMg / by));

    private static async Task<NutritionResponse> Calculate(NutritionService nutrition, int servings,
        List<(string Name, decimal Quantity, string Unit)> ingredients, CancellationToken ct)
    {
        if (!nutrition.IsAvailable) return new NutritionResponse(false, servings, null, 0, ingredients.Count, []);

        var facts = await nutrition.FactsForAsync(ingredients.Select(i => i.Name), ct);
        var (totals, lines) = NutritionCalculator.Calculate(
            ingredients.Select(i => (i.Name, i.Quantity, i.Unit, facts.GetValueOrDefault(i.Name.Trim().ToLowerInvariant()))));
        return new NutritionResponse(true, servings, PerServing(totals, servings),
            lines.Count(l => l.Counted), lines.Count,
            lines.Select(l => new NutritionLineResponse(l.Ingredient, l.MatchedFood, l.Grams, l.Calories, l.Counted, l.Approximate)).ToList());
    }
}
