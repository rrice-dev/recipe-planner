using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Photos;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

// Photos of the original cookbook page or recipe card, kept with the typed-up recipe.
// Only the owner can ever see them: there's no shared route, unlike the recipe's own photo.
public static class SourcePages
{
    public static void MapSourcePageEndpoints(this IEndpointRouteBuilder app)
    {
        var pages = app.MapGroup("/api/recipes/{id:int}/source-pages").RequireAuthorization();

        // One page per request, as raw image bytes (same rules as every other upload; see PhotoUpload)
        pages.MapPost("/", async (int id, HttpRequest request, ClaimsPrincipal user, AppDbContext db, IPhotoStore store,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (!store.IsAvailable)
                return Results.Problem("Photo uploads aren't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var recipe = await db.Recipes.Include(r => r.SourcePages)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == user.UserId(), ct);
            if (recipe is null) return Results.NotFound();
            if (recipe.SourcePages.Count >= RecipeReading.MaxPages)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Photo"] = [$"A recipe can keep up to {RecipeReading.MaxPages} original pages."] });

            var (bytes, uploadError) = await PhotoUpload.ReadAsync(request, ct);
            if (uploadError is not null) return uploadError;

            var photo = PhotoProcessor.Process(bytes!, RecipeReading.KeepMaxEdge, RecipeReading.KeepJpegQuality);
            if (!photo.Success)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Photo"] = [photo.Error!] });

            var name = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}.jpg";
            try
            {
                await store.SaveAsync(name, photo.Jpeg, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                loggerFactory.CreateLogger("SourcePages").LogError(ex, "Saving an original page failed");
                return Results.Problem("Couldn't save the photo right now. Try again in a minute.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var page = new RecipeSourcePage
            {
                RecipeId = recipe.Id,
                PhotoName = name,
                Position = recipe.SourcePages.Count == 0 ? 1 : recipe.SourcePages.Max(p => p.Position) + 1
            };
            db.RecipeSourcePages.Add(page);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new SourcePageResponse(page.Id, page.Position));
        })
        .RequireRateLimiting(RateLimiting.PhotoUploadPolicy);

        pages.MapGet("/{pageId:int}", async (int id, int pageId, ClaimsPrincipal user, AppDbContext db, IPhotoStore store,
            HttpContext http, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var name = await db.RecipeSourcePages.AsNoTracking()
                .Where(p => p.Id == pageId && p.RecipeId == id
                            && db.Recipes.Any(r => r.Id == id && r.OwnerId == userId))
                .Select(p => p.PhotoName)
                .FirstOrDefaultAsync(ct);
            if (name is null) return Results.NotFound();
            var bytes = await store.ReadAsync(name, ct);
            if (bytes is null) return Results.NotFound();

            // A page never changes once saved (it's deleted, not replaced), so its URL can be cached,
            // but only by the owner's own browser
            http.Response.Headers.CacheControl = "private, max-age=604800, immutable";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(bytes, "image/jpeg");
        });

        pages.MapDelete("/{pageId:int}", async (int id, int pageId, ClaimsPrincipal user, AppDbContext db, IPhotoStore store) =>
        {
            var userId = user.UserId();
            var page = await db.RecipeSourcePages
                .FirstOrDefaultAsync(p => p.Id == pageId && p.RecipeId == id
                                          && db.Recipes.Any(r => r.Id == id && r.OwnerId == userId));
            if (page is null) return Results.NotFound();

            db.RecipeSourcePages.Remove(page);
            await db.SaveChangesAsync();
            await store.DeleteAsync(page.PhotoName, CancellationToken.None);
            return Results.NoContent();
        });
    }

    public static List<SourcePageResponse> ToResponse(IEnumerable<RecipeSourcePage> pages) =>
        pages.OrderBy(p => p.Position).Select(p => new SourcePageResponse(p.Id, p.Position)).ToList();
}
