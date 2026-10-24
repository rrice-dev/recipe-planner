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

        if (!await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            await userManager.AddToRoleAsync(user, Roles.Admin);
            logger.LogInformation("Granted the Admin role to the configured admin account.");
        }
    }
}
