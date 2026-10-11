using Microsoft.AspNetCore.Identity;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Built-in: /register, /login, /refresh, /manage/info, /forgotPassword, and more
        group.MapIdentityApi<AppUser>();

        // Identity doesn't include logout, so we add it
        group.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }
}