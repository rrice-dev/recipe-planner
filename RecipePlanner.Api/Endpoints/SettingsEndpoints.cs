using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Photos;

namespace RecipePlanner.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings").RequireAuthorization();

        settings.MapGet("/", async (ClaimsPrincipal user, UserManager<AppUser> users) =>
        {
            var me = await users.GetUserAsync(user);
            return me is null ? Results.Unauthorized() : Results.Ok(ToResponse(me));
        });

        settings.MapPut("/", async (SettingsRequest request, ClaimsPrincipal user, UserManager<AppUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!UserSettings.Themes.Contains(request.Theme))
                errors["Theme"] = [$"Theme must be one of: {string.Join(", ", UserSettings.Themes)}."];
            if (request.Diet is not null && !Diet.IsKnown(request.Diet))
                errors["Diet"] = [$"Diet must be empty or one of: {string.Join(", ", UserSettings.Diets)}."];
            if (request.DefaultServings is < 1 or > UserSettings.MaxServings)
                errors["DefaultServings"] = [$"Default servings must be between 1 and {UserSettings.MaxServings}."];
            if (request.Units is not null && !UserSettings.UnitSystems.Contains(request.Units))
                errors["Units"] = [$"Units must be one of: {string.Join(", ", UserSettings.UnitSystems)}."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var me = await users.GetUserAsync(user);
            if (me is null) return Results.Unauthorized();
            me.Theme = request.Theme;
            me.Diet = request.Diet;
            me.DefaultServings = request.DefaultServings;
            if (request.Units is not null) me.Units = request.Units;
            await users.UpdateAsync(me);
            return Results.Ok(ToResponse(me));
        });

        // Deletes the account and everything in it. Asks for the password again, so a session
        // left open on someone else's computer can't be used to wipe the account.
        app.MapPost("/api/account/delete", async (
            DeleteAccountRequest request,
            ClaimsPrincipal user,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            AppDbContext db,
            IPhotoStore photos,
            ILoggerFactory loggerFactory) =>
        {
            var me = await users.GetUserAsync(user);
            if (me is null) return Results.Unauthorized();
            if (string.IsNullOrEmpty(request.Password) || !await users.CheckPasswordAsync(me, request.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Password"] = ["That password isn't correct."] });

            // Photos live in Blob storage, outside the database, so they're removed explicitly.
            // Everything else (recipes, plans, lists, ratings, Kroger link) is deleted by the database
            // along with the user.
            var photoNames = await db.Recipes.Where(r => r.OwnerId == me.Id && r.PhotoName != null)
                .Select(r => r.PhotoName!).ToListAsync();
            var log = loggerFactory.CreateLogger("SettingsEndpoints");
            foreach (var name in photoNames)
            {
                try { await photos.DeleteAsync(name, CancellationToken.None); }
                catch (Exception ex) { log.LogWarning(ex, "Couldn't delete a photo while deleting an account"); }
            }

            var result = await users.DeleteAsync(me);
            if (!result.Succeeded)
                return Results.Problem("Couldn't delete the account. Please try again.", statusCode: StatusCodes.Status500InternalServerError);

            await signIn.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(); // also on the strict login rate limit (see RateLimiting.SensitiveAuthPaths)
    }

    private static SettingsResponse ToResponse(AppUser u) => new(u.Theme, u.Diet, u.DefaultServings, u.Units);
}
