using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
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
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                // The AI being down shouldn't break the page: still return the recipe matches
                loggerFactory.CreateLogger("FridgeEndpoints").LogWarning(ex, "Meal idea generation failed");
                return Results.Ok(new FridgeResponse(matches, [], true, "AI suggestions are unavailable right now. Try again in a minute."));
            }
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.AiPolicy);
    }
}
