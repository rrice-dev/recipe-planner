using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

// The world map: library recipes come from GET /api/library (each has a country);
// these endpoints add AI suggestions for any country, and full recipes for them.
public static class WorldEndpoints
{
    public const int MaxCountryLength = 60;
    public const int MaxDishes = 6;
    private const int MaxDescriptionLength = 300;

    // Every user who clicks the same country gets the same list for a day, so it's one AI call per country, not per click
    public static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    public static void MapWorldEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/world/dishes", async (
            CountryDishesRequest request,
            RecipeLibrary library,
            ICountryDishGenerator generator,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var country = (request.Country ?? "").Trim();
            if (ValidateCountry(country) is { } errors) return Results.ValidationProblem(errors);

            if (!generator.IsAvailable)
                return Results.Ok(new CountryDishesResponse(country, [], false, "AI suggestions aren't set up yet."));

            var key = "world-dishes:" + country.ToLowerInvariant();
            if (cache.TryGetValue(key, out List<CountryDish>? cached))
                return Results.Ok(new CountryDishesResponse(country, cached!, true, null));

            var inLibrary = library.ForCountry(country).Select(r => r.Title).ToList();
            try
            {
                var raw = await generator.GenerateAsync(country, inLibrary, ct);
                var dishes = Clean(raw, inLibrary);
                if (dishes.Count > 0) cache.Set(key, dishes, CacheFor); // don't cache a failure or an empty answer
                return Results.Ok(new CountryDishesResponse(country, dishes, true,
                    dishes.Count == 0 ? "No suggestions for that place. Try another country." : null));
            }
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                loggerFactory.CreateLogger("WorldEndpoints").LogWarning(ex, "Country dish generation failed");
                return Results.Ok(new CountryDishesResponse(country, [], true,
                    "AI suggestions are unavailable right now. Try again in a minute."));
            }
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.AiPolicy);

        // Full recipe for one suggested dish, in the same shape as the fridge page's recipes
        app.MapPost("/api/world/recipe", async (
            CountryRecipeRequest request,
            AppDbContext db,
            IRecipeDraftGenerator generator,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!generator.IsAvailable)
                return Results.Problem("AI recipes aren't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var country = (request.Country ?? "").Trim();
            var dish = (request.DishName ?? "").Trim();
            var errors = ValidateCountry(country) ?? new Dictionary<string, string[]>();
            if (dish.Length == 0)
                errors["DishName"] = ["Dish name is required."];
            else if (dish.Length > FridgePlanner.MaxDishNameLength)
                errors["DishName"] = [$"Dish name must be {FridgePlanner.MaxDishNameLength} characters or fewer."];
            else if (!FridgePlanner.IsSafeName(dish))
                errors["DishName"] = ["Dish name can only contain letters, numbers, spaces and basic punctuation."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var techniques = await db.Techniques.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
            try
            {
                var raw = await generator.GenerateAsync($"{dish} (from {country})", [], techniques.Select(t => t.Name).ToList(), ct);
                var recipe = FridgePlanner.CleanRecipe(raw, [], techniques);
                return recipe is null
                    ? Results.Problem("The AI didn't return a usable recipe. Try again.", statusCode: StatusCodes.Status502BadGateway)
                    : Results.Ok(recipe);
            }
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                loggerFactory.CreateLogger("WorldEndpoints").LogWarning(ex, "Country recipe generation failed");
                return Results.Problem("AI recipes are unavailable right now. Try again in a minute.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.AiRecipePolicy);
    }

    private static Dictionary<string, string[]>? ValidateCountry(string country)
    {
        if (country.Length == 0) return new() { ["Country"] = ["Country is required."] };
        if (country.Length > MaxCountryLength) return new() { ["Country"] = [$"Country must be {MaxCountryLength} characters or fewer."] };
        if (!FridgePlanner.IsSafeName(country)) return new() { ["Country"] = ["Country can only contain letters, spaces and basic punctuation."] };
        return null;
    }

    public static List<CountryDish> Clean(IEnumerable<RawCountryDish> raw, IReadOnlyList<string> inLibrary) =>
        raw.Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new CountryDish(
                d.Name!.Trim(),
                Truncate((d.Description ?? "").Trim(), MaxDescriptionLength),
                d.AipFriendly ?? false))
            .Where(d => d.Name.Length <= FridgePlanner.MaxDishNameLength && FridgePlanner.IsSafeName(d.Name))
            .Where(d => !inLibrary.Contains(d.Name, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDishes)
            .ToList();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
