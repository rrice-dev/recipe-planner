using Microsoft.AspNetCore.Identity;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Data;

public static class AdminSeeder
{
    public static async Task EnsureAdminAsync(IServiceProvider services, string? adminEmail)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            await roleManager.CreateAsync(new IdentityRole(Roles.Admin));

        if (string.IsNullOrWhiteSpace(adminEmail)) return;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminSeeder));
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(adminEmail);
        if (user is null)
        {
            // Not an error: register that account, then restart the API
            logger.LogWarning("Admin:Email is set, but no account with that email exists yet.");
            return;
        }

        // Anyone can register any address, so a matching email alone proves nothing. Only an account that
        // has confirmed the address (by following the emailed link) gets the role.
        if (!user.EmailConfirmed)
        {
            if (!await userManager.IsInRoleAsync(user, Roles.Admin))
                logger.LogWarning("Admin:Email matches an account that hasn't confirmed its email, so it wasn't made admin. " +
                                  "Confirm the email from that account, then restart the API.");
            return;
        }

        // The admin approves everyone else, so nobody would be left to approve the admin
        if (!user.IsApproved)
        {
            user.IsApproved = true;
            await userManager.UpdateAsync(user);
        }

        if (await userManager.IsInRoleAsync(user, Roles.Admin)) return;
        await userManager.AddToRoleAsync(user, Roles.Admin);
        logger.LogInformation("Granted the Admin role to the configured admin account.");
    }
}
