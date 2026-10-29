using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Tests;

public class AdminSeederTests
{
    private static async Task<bool> SeedAndCheck(ApiFactory factory, string email, bool confirmed)
    {
        await factory.CreateUserClientAsync(email);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        if (confirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        // What startup runs with Admin:Email set to this address
        await AdminSeeder.EnsureAdminAsync(scope.ServiceProvider, email);
        return await users.IsInRoleAsync(user, Roles.Admin);
    }

    [Fact]
    public async Task An_unconfirmed_account_with_the_admin_email_is_not_made_admin()
    {
        using var factory = new ApiFactory();
        Assert.False(await SeedAndCheck(factory, $"squatter-{Guid.NewGuid():N}@test.local", confirmed: false));
    }

    [Fact]
    public async Task A_confirmed_account_with_the_admin_email_is_made_admin()
    {
        using var factory = new ApiFactory();
        Assert.True(await SeedAndCheck(factory, $"owner-{Guid.NewGuid():N}@test.local", confirmed: true));
    }
}
