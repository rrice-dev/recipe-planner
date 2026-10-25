using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Photos;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

public static class RecipePhotos
{
    public const int MaxEdge = 1600;      // Sharp on phones and laptops, still only ~150-300 KB
    public const int JpegQuality = 82;

    // A short value that changes whenever the photo does. Image URLs include it (?v=...), so browsers
    // can cache a photo for a long time but still fetch the new one immediately after a change.
    public static string? Version(Recipe r) =>
        r.PhotoName is null ? null : r.PhotoUpdatedAt?.ToUnixTimeMilliseconds().ToString() ?? "1";

    public static void MapRecipePhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/recipes/{id:int}/photo").RequireAuthorization();

        mine.MapPut("/", async (int id, HttpRequest request, ClaimsPrincipal user, AppDbContext db, IPhotoStore store,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (!store.IsAvailable)
                return Results.Problem("Photo uploads aren't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == user.UserId(), ct);
            if (recipe is null) return Results.NotFound();

            var (bytes, uploadError) = await PhotoUpload.ReadAsync(request, ct);
            if (uploadError is not null) return uploadError;

            // Same checks as fridge scans: real image, sane dimensions, re-encoded (no EXIF/GPS), resized
            var photo = PhotoProcessor.Process(bytes!, MaxEdge, JpegQuality);
            if (!photo.Success)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Photo"] = [photo.Error!] });

            // Save the new file under a new random name first, then point the recipe at it, then delete
            // the old one. If anything fails partway, the recipe never points at a missing photo.
            var newName = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}.jpg";
            try
            {
                await store.SaveAsync(newName, photo.Jpeg, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                loggerFactory.CreateLogger("RecipePhotos").LogError(ex, "Saving a recipe photo failed");
                return Results.Problem("Couldn't save the photo right now. Try again in a minute.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var oldName = recipe.PhotoName;
            recipe.PhotoName = newName;
            recipe.PhotoUpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            if (oldName is not null) await store.DeleteAsync(oldName, CancellationToken.None);

            return Results.Ok(new { photoVersion = Version(recipe) });
        })
        .RequireRateLimiting(RateLimiting.PhotoUploadPolicy);

        mine.MapDelete("/", async (int id, ClaimsPrincipal user, AppDbContext db, IPhotoStore store) =>
        {
            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == user.UserId());
            if (recipe is null) return Results.NotFound();

            var oldName = recipe.PhotoName;
            recipe.PhotoName = null;
            recipe.PhotoUpdatedAt = null;
            await db.SaveChangesAsync();
            if (oldName is not null) await store.DeleteAsync(oldName, CancellationToken.None);
            return Results.NoContent();
        });

        // Your own photo: same ownership rule as the recipe itself
        mine.MapGet("/", async (int id, ClaimsPrincipal user, AppDbContext db, IPhotoStore store, HttpContext http, CancellationToken ct) =>
        {
            var name = await db.Recipes.AsNoTracking()
                .Where(r => r.Id == id && r.OwnerId == user.UserId())
                .Select(r => r.PhotoName)
                .FirstOrDefaultAsync(ct);
            return await Serve(store, name, http, isPublic: false, ct);
        });

        // Shared photo: anyone with the link, like the shared recipe. Social sites load this for preview cards.
        app.MapGet("/api/shared/{token}/photo", async (string token, AppDbContext db, IPhotoStore store, HttpContext http, CancellationToken ct) =>
        {
            if (!SharingEndpoints.IsTokenShaped(token)) return Results.NotFound();
            var name = await db.Recipes.AsNoTracking()
                .Where(r => r.ShareToken == token)
                .Select(r => r.PhotoName)
                .FirstOrDefaultAsync(ct);
            return await Serve(store, name, http, isPublic: true, ct);
        });
    }

    private static async Task<IResult> Serve(IPhotoStore store, string? name, HttpContext http, bool isPublic, CancellationToken ct)
    {
        if (name is null) return Results.NotFound();
        var bytes = await store.ReadAsync(name, ct);
        if (bytes is null) return Results.NotFound();

        // URLs carry ?v=<version>, so the file at a given URL never changes and can be cached for a long time.
        // Private photos may only be cached by the user's own browser, never by shared caches.
        http.Response.Headers.CacheControl = isPublic ? "public, max-age=86400" : "private, max-age=86400";
        // Belt and braces: even if something odd got through, the browser treats it strictly as an image
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(bytes, "image/jpeg");
    }
}
