using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static partial class SharingEndpoints
{
    // 16 random bytes = 128 bits, written as 22 URL-safe characters. Guessing a valid link is not feasible,
    // unlike recipe IDs, which count up 1, 2, 3...
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("^[A-Za-z0-9_-]{22}$")]
    private static partial Regex TokenFormat();

    public static void MapSharingEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Owner: turn sharing on and off, and rate your own recipe ----------

        var mine = app.MapGroup("/api/recipes/{id:int}").RequireAuthorization();

        mine.MapPost("/share", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == user.UserId());
            if (recipe is null) return Results.NotFound();

            if (recipe.ShareToken is null) // sharing an already-shared recipe keeps the same link
            {
                recipe.ShareToken = NewToken();
                recipe.SharedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { shareToken = recipe.ShareToken });
        });

        mine.MapDelete("/share", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == user.UserId());
            if (recipe is null) return Results.NotFound();

            // The old link stops working immediately. Sharing again creates a brand-new link.
            recipe.ShareToken = null;
            recipe.SharedAt = null;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        mine.MapPut("/rating", async (int id, RatingRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (Ratings.Validate(request) is { } errors) return Results.ValidationProblem(errors);
            var userId = user.UserId();
            // Rating by ID is only for your own recipes; anyone else has to go through a share link
            if (!await db.Recipes.AnyAsync(r => r.Id == id && r.OwnerId == userId)) return Results.NotFound();

            await Ratings.SetAsync(db, id, userId, request.Stars);
            return Results.NoContent();
        });

        mine.MapDelete("/rating", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            if (!await db.Recipes.AnyAsync(r => r.Id == id && r.OwnerId == userId)) return Results.NotFound();
            await Ratings.ClearAsync(db, id, userId);
            return Results.NoContent();
        });

        // ---------- Anyone with the link ----------

        var shared = app.MapGroup("/api/shared/{token}");

        // No login needed to view
        shared.MapGet("/", async (string token, ClaimsPrincipal user, AppDbContext db) =>
        {
            var recipe = await FindShared(db, token, includeDetails: true);
            if (recipe is null) return Results.NotFound();

            var userId = user.Identity?.IsAuthenticated == true ? user.UserId() : null;
            var (average, count) = Ratings.Community(recipe.Ratings, recipe.OwnerId);

            return Results.Ok(new SharedRecipeResponse(
                recipe.Title, recipe.Description, recipe.Cuisine,
                recipe.Servings, recipe.PrepMinutes, recipe.CookMinutes,
                recipe.Techniques.OrderBy(t => t.Name).Select(t => t.Name).ToList(),
                recipe.Ingredients.OrderBy(i => i.Ingredient.Name)
                    .Select(i => new SharedIngredient(i.Ingredient.Name, i.Quantity, i.Unit, i.Note)).ToList(),
                recipe.Steps.OrderBy(s => s.Order)
                    .Select(s => new SharedStep(s.Order, s.Instruction, s.Technique?.Name)).ToList(),
                new RatingSummary(
                    average, count,
                    userId is null ? null : recipe.Ratings.FirstOrDefault(r => r.UserId == userId)?.Stars,
                    userId == recipe.OwnerId)));
        });

        // Rating requires an account, which keeps anonymous ballot-stuffing out
        shared.MapPut("/rating", async (string token, RatingRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (Ratings.Validate(request) is { } errors) return Results.ValidationProblem(errors);
            var recipe = await FindShared(db, token, includeDetails: false);
            if (recipe is null) return Results.NotFound();

            await Ratings.SetAsync(db, recipe.Id, user.UserId(), request.Stars);
            return Results.NoContent();
        }).RequireAuthorization();

        shared.MapDelete("/rating", async (string token, ClaimsPrincipal user, AppDbContext db) =>
        {
            var recipe = await FindShared(db, token, includeDetails: false);
            if (recipe is null) return Results.NotFound();
            await Ratings.ClearAsync(db, recipe.Id, user.UserId());
            return Results.NoContent();
        }).RequireAuthorization();

        // ---------- Link previews for social media ----------

        // Facebook, X, iMessage, Discord and others fetch a shared link to build its preview card, but they
        // don't run JavaScript, so the Angular app looks blank to them. This tiny server-rendered page gives
        // them a title and description, then sends people on to the real page.
        app.MapGet("/s/{token}", async (string token, AppDbContext db, HttpContext http) =>
        {
            var recipe = await FindShared(db, token, includeDetails: false);
            http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
            http.Response.Headers["X-Robots-Tag"] = "noindex"; // shared, but not meant to show up in search engines

            if (recipe is null)
                return Results.Content(Page("Recipe not found", "This recipe isn't shared anymore.", null),
                    "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);

            var description = string.IsNullOrWhiteSpace(recipe.Description)
                ? $"Serves {recipe.Servings} · {recipe.PrepMinutes + recipe.CookMinutes} min · Recipe Planner"
                : recipe.Description;
            return Results.Content(Page(recipe.Title, description, $"/r/{token}"), "text/html; charset=utf-8");
        });
    }

    private static async Task<Recipe?> FindShared(AppDbContext db, string token, bool includeDetails)
    {
        // Reject anything that isn't token-shaped before it gets near the database
        if (!TokenFormat().IsMatch(token)) return null;

        IQueryable<Recipe> query = db.Recipes.AsNoTracking().Include(r => r.Ratings);
        if (includeDetails)
            query = query
                .Include(r => r.Techniques)
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps).ThenInclude(s => s.Technique)
                .AsSplitQuery();
        return await query.FirstOrDefaultAsync(r => r.ShareToken == token);
    }

    // Every value is HTML-encoded: recipe titles are user input, and this page is built as a string
    private static string Page(string title, string description, string? redirect)
    {
        var t = WebUtility.HtmlEncode(Shorten(title, 100));
        var d = WebUtility.HtmlEncode(Shorten(description, 200));
        var refresh = redirect is null ? "" : $"""<meta http-equiv="refresh" content="0; url={redirect}">""";
        var link = redirect is null ? "" : $"""<p><a href="{redirect}">Open the recipe</a></p>""";
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{t}</title>
            <meta name="description" content="{d}">
            <meta property="og:type" content="article">
            <meta property="og:site_name" content="Recipe Planner">
            <meta property="og:title" content="{t}">
            <meta property="og:description" content="{d}">
            <meta name="twitter:card" content="summary">
            <meta name="twitter:title" content="{t}">
            <meta name="twitter:description" content="{d}">
            {refresh}
            </head>
            <body style="font-family: sans-serif; padding: 2rem;">
            <h1>{t}</h1>
            <p>{d}</p>
            {link}
            </body>
            </html>
            """;
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
