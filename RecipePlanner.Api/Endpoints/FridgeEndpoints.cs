using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Photos;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

public static class FridgeEndpoints
{
    public static void MapFridgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/fridge/suggestions", async (
            FridgeRequest request,
            ClaimsPrincipal user,
            AppDbContext db,
            IMealIdeaGenerator ideas,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var (onHand, errors) = FridgePlanner.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // Only this user's recipes. The AI never sees them, and never sees other users' data.
            var userId = user.UserId();
            var recipes = await db.Recipes.AsNoTracking()
                .Where(r => r.OwnerId == userId)
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .ToListAsync(ct);
            var matches = FridgePlanner.MatchRecipes(recipes, onHand);

            if (!ideas.IsAvailable)
                return Results.Ok(new FridgeResponse(matches, [], false, "AI suggestions aren't set up yet."));

            var techniques = await db.Techniques.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
            try
            {
                var raw = await ideas.GenerateAsync(onHand, techniques.Select(t => t.Name).ToList(), ct);
                return Results.Ok(new FridgeResponse(matches, FridgePlanner.CleanIdeas(raw, techniques), true, null));
            }
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                // The AI being down shouldn't break the page: still return the recipe matches
                loggerFactory.CreateLogger("FridgeEndpoints").LogWarning(ex, "Meal idea generation failed");
                return Results.Ok(new FridgeResponse(matches, [], true, "AI suggestions are unavailable right now. Try again in a minute."));
            }
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.AiPolicy);

        // Full recipe for one AI idea: ingredients with amounts, and steps linked to real techniques
        app.MapPost("/api/fridge/recipe", async (
            FridgeRecipeRequest request,
            AppDbContext db,
            IRecipeDraftGenerator generator,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!generator.IsAvailable)
                return Results.Problem("AI recipes aren't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var (dish, onHand, errors) = FridgePlanner.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var techniques = await db.Techniques.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
            try
            {
                var raw = await generator.GenerateAsync(dish, onHand, techniques.Select(t => t.Name).ToList(), ct);
                var recipe = FridgePlanner.CleanRecipe(raw, onHand, techniques);
                return recipe is null
                    ? Results.Problem("The AI didn't return a usable recipe. Try again.", statusCode: StatusCodes.Status502BadGateway)
                    : Results.Ok(recipe);
            }
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                loggerFactory.CreateLogger("FridgeEndpoints").LogWarning(ex, "Recipe generation failed");
                return Results.Problem("AI recipes are unavailable right now. Try again in a minute.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.AiRecipePolicy);

        // The body is the raw image bytes; see PhotoUpload for why that's safer than a form
        app.MapPost("/api/fridge/scan", async (
            HttpRequest request,
            IIngredientScanner scanner,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!scanner.IsAvailable)
                return Results.Problem("Photo scanning isn't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var (bytes, uploadError) = await PhotoUpload.ReadAsync(request, ct);
            if (uploadError is not null) return uploadError;

            var photo = PhotoProcessor.Process(bytes!);
            if (!photo.Success)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Photo"] = [photo.Error!] });

            try
            {
                var raw = await scanner.ScanAsync(photo.Jpeg, ct);
                return Results.Ok(new ScanResponse(FridgePlanner.CleanScannedIngredients(raw)));
            }
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                loggerFactory.CreateLogger("FridgeEndpoints").LogWarning(ex, "Photo scan failed");
                return Results.Problem("Photo scanning is unavailable right now. Try again in a minute.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.AiScanPolicy);
    }
}
