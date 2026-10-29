using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public record AdminUserResponse(string Id, string Email, bool EmailConfirmed, bool IsApproved);

// Approving new accounts (see Security/AccountApproval.cs). Admins only.
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users").RequireAuthorization(Roles.Admin);

        // Waiting accounts first, then everyone else
        group.MapGet("/", async (UserManager<AppUser> users) =>
            Results.Ok(await users.Users.AsNoTracking()
                .OrderBy(u => u.IsApproved).ThenBy(u => u.Email)
                .Select(u => new AdminUserResponse(u.Id, u.Email ?? "", u.EmailConfirmed, u.IsApproved))
                .ToListAsync()));

        group.MapPost("/{id}/approve", async (string id, UserManager<AppUser> users, ILogger<AdminUserResponse> logger) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (!user.IsApproved)
            {
                user.IsApproved = true;
                await users.UpdateAsync(user);
                logger.LogInformation("Account {UserId} approved", user.Id);
            }
            return Results.NoContent();
        });

        // Declining removes the account. Only for accounts still waiting: an approved account has recipes
        // and photos, and its owner deletes it from Settings.
        group.MapDelete("/{id}", async (string id, UserManager<AppUser> users, ILogger<AdminUserResponse> logger) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (user.IsApproved)
                return Results.Problem("Only accounts still waiting for approval can be declined.", statusCode: StatusCodes.Status409Conflict);
            await users.DeleteAsync(user);
            logger.LogInformation("Waiting account {UserId} declined and deleted", id);
            return Results.NoContent();
        });
    }
}
