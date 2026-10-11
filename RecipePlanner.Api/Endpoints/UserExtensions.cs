using System.Security.Claims;

namespace RecipePlanner.Api.Endpoints;

internal static class UserExtensions
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No user ID on an authenticated request.");
}