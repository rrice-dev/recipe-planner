using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Kroger;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Nutrition;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

// Estimated cost from your Kroger store's current prices: per recipe, and for a planned week against a budget
public static class CostEndpoints
{
    public const decimal MaxBudget = 100_000m;

    private record Priced(int RecipeId, decimal? Total, int Counted, int Count, List<CostLine> Lines);

    public static void MapCostEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/recipes/{id:int}/cost", async (int id, int? servings, ClaimsPrincipal user, AppDbContext db,
            IOptions<KrogerOptions> kroger, PriceService prices, NutritionService nutrition, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.AsNoTracking().Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId, ct);
            if (recipe is null) return Results.NotFound();
            var make = servings ?? recipe.Servings;
            if (make is < 1 or > UserSettings.MaxServings)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Servings"] = ["Servings must be between 1 and 100."] });

            var (status, store) = await StoreFor(db, userId, kroger.Value, ct);
            if (status != "ok") return Results.Ok(new RecipeCostResponse(status, null, make, null, null, 0, recipe.Ingredients.Count, []));

            try
            {
                var priced = (await Price(db, prices, nutrition, store!.LocationId!, userId, [recipe], ct))[recipe.Id];
                var factor = (decimal)make / Math.Max(recipe.Servings, 1);
                var total = priced.Total is { } t ? Money(t * factor) : (decimal?)null;
                return Results.Ok(new RecipeCostResponse("ok", store.LocationName, make, total,
                    total is { } tt ? Money(tt / make) : null, priced.Counted, priced.Count,
                    priced.Lines.Select(l => l with { Cost = l.Cost is { } c ? Money(c * factor) : null }).ToList()));
            }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException) { return KrogerDown(); }
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.KrogerPolicy);

        app.MapGet("/api/mealplans/{id:int}/cost", async (int id, ClaimsPrincipal user, AppDbContext db, UserManager<AppUser> users,
            IOptions<KrogerOptions> kroger, PriceService prices, NutritionService nutrition, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId, ct);
            if (plan is null) return Results.NotFound();
            var budget = (await users.GetUserAsync(user))?.WeeklyBudget;

            var (status, store) = await StoreFor(db, userId, kroger.Value, ct);
            if (status != "ok") return Results.Ok(new PlanCostResponse(status, null, [], null, budget, false));

            try
            {
                var recipes = plan.Entries.Select(e => e.Recipe).DistinctBy(r => r.Id).ToList();
                var priced = await Price(db, prices, nutrition, store!.LocationId!, userId, recipes, ct);
                var meals = plan.Entries.OrderBy(e => e.Day).ThenBy(e => e.Id).Select(e =>
                {
                    var p = priced[e.RecipeId];
                    var cost = p.Total is { } t ? Money(t * e.Servings / Math.Max(e.Recipe.Servings, 1)) : (decimal?)null;
                    return new PlanMealCost(e.Day, e.RecipeId, e.Recipe.Title, e.Servings, cost,
                        cost is { } c && e.Servings > 0 ? Money(c / e.Servings) : null, p.Counted == p.Count);
                }).ToList();
                var total = meals.Any(m => m.Cost is not null) ? meals.Sum(m => m.Cost ?? 0) : (decimal?)null;
                return Results.Ok(new PlanCostResponse("ok", store.LocationName, meals, total, budget, meals.All(m => m.Complete)));
            }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException) { return KrogerDown(); }
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.KrogerPolicy);

        app.MapGet("/api/settings/budget", async (ClaimsPrincipal user, UserManager<AppUser> users) =>
            await users.GetUserAsync(user) is { } me ? Results.Ok(new BudgetSettings(me.WeeklyBudget)) : Results.Unauthorized())
            .RequireAuthorization();

        app.MapPut("/api/settings/budget", async (BudgetSettings request, ClaimsPrincipal user, UserManager<AppUser> users) =>
        {
            if (request.WeeklyBudget is <= 0 or > MaxBudget)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["WeeklyBudget"] = ["Enter a weekly budget above $0, or leave it empty."] });
            var me = await users.GetUserAsync(user);
            if (me is null) return Results.Unauthorized();
            me.WeeklyBudget = request.WeeklyBudget is { } b ? Math.Round(b, 2, MidpointRounding.AwayFromZero) : null;
            await users.UpdateAsync(me);
            return Results.Ok(new BudgetSettings(me.WeeklyBudget));
        })
        .RequireAuthorization();
    }

    private static async Task<(string Status, KrogerConnection? Store)> StoreFor(AppDbContext db, string userId, KrogerOptions options, CancellationToken ct)
    {
        if (!options.IsConfigured) return ("unavailable", null);
        var c = await db.KrogerConnections.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        return c?.LocationId is null ? ("no-store", null) : ("ok", c);
    }

    // Each recipe as written (its own servings). Food weights from USDA, when set up, turn "1 cup" into grams.
    private static async Task<Dictionary<int, Priced>> Price(AppDbContext db, PriceService prices, NutritionService nutrition,
        string locationId, string userId, List<Recipe> recipes, CancellationToken ct)
    {
        var names = recipes.SelectMany(r => r.Ingredients.Select(i => i.Ingredient.Name)).ToList();
        var products = await prices.ProductsForAsync(locationId, userId, names, ct);
        var foods = nutrition.IsAvailable ? await nutrition.FactsForAsync(names, ct) : [];

        return recipes.ToDictionary(r => r.Id, r =>
        {
            var lines = r.Ingredients.OrderBy(i => i.Ingredient.Name).Select(i =>
            {
                var product = products.GetValueOrDefault(PriceService.Key(i.Ingredient.Name));
                var food = foods.GetValueOrDefault(i.Ingredient.Name.Trim().ToLowerInvariant());
                var cost = product is null ? null : PriceEstimator.CostOf(i.Quantity, i.Unit, product.Price, product.Size, food);
                return new CostLine(i.Ingredient.Name, product?.Description, product?.Size, product?.Price, cost?.Cost, cost?.Approximate ?? false);
            }).ToList();
            var counted = lines.Count(l => l.Cost is not null);
            return new Priced(r.Id, counted == 0 ? null : lines.Sum(l => l.Cost ?? 0), counted, lines.Count, lines);
        });
    }

    private static decimal Money(decimal d) => Math.Round(d, 2, MidpointRounding.AwayFromZero);

    private static IResult KrogerDown() =>
        Results.Problem("Kroger isn't responding right now. Try again in a minute.", statusCode: StatusCodes.Status502BadGateway);
}
