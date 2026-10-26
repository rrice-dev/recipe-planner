using System.Security.Claims;
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

        app.MapGet("/api/library/{slug}/nutrition", async (string slug, RecipeLibrary library, NutritionService nutrition, CancellationToken ct) =>
        {
            var r = library.Find(slug);
            if (r is null) return Results.NotFound();
            return Results.Ok(await Calculate(nutrition, r.Servings, r.Ingredients.Select(i => (i.Name, i.Quantity, i.Unit)).ToList(), ct));
        })
        .RequireRateLimiting(RateLimiting.NutritionPolicy);
    }

    private static async Task<NutritionResponse> Calculate(NutritionService nutrition, int servings,
        List<(string Name, decimal Quantity, string Unit)> ingredients, CancellationToken ct)
    {
        if (!nutrition.IsAvailable) return new NutritionResponse(false, servings, null, 0, ingredients.Count, []);

        var facts = await nutrition.FactsForAsync(ingredients.Select(i => i.Name), ct);
        var (totals, lines) = NutritionCalculator.Calculate(
            ingredients.Select(i => (i.Name, i.Quantity, i.Unit, facts.GetValueOrDefault(i.Name.Trim().ToLowerInvariant()))));
        var per = Math.Max(servings, 1);
        return new NutritionResponse(true, servings,
            new NutrientsResponse((int)Math.Round(totals.Calories / per), Math.Round(totals.Protein / per, 1), Math.Round(totals.Carbs / per, 1),
                Math.Round(totals.Fat / per, 1), Math.Round(totals.Fiber / per, 1), Math.Round(totals.Sugar / per, 1), (int)Math.Round(totals.SodiumMg / per)),
            lines.Count(l => l.Counted), lines.Count,
            lines.Select(l => new NutritionLineResponse(l.Ingredient, l.MatchedFood, l.Grams, l.Calories, l.Counted, l.Approximate)).ToList());
    }
}
