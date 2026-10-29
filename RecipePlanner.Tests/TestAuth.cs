using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Tests;

public static class TestAuth
{
    public const string Password = "correct-horse-battery";

    public static async Task<HttpClient> CreateUserClientAsync(this ApiFactory factory, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient(); // keeps cookies between requests

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        register.EnsureSuccessStatusCode();

        await ApproveAsync(factory, email); // new accounts wait for an admin (see AccountApprovalTests)
        await LoginAsync(client, email);
        return client;
    }

    /// <summary>What an admin's "Approve" does</summary>
    public static async Task ApproveAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        user.IsApproved = true;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    public static async Task<HttpClient> CreateAdminClientAsync(this ApiFactory factory)
    {
        var email = $"admin-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        register.EnsureSuccessStatusCode();

        // Grant the role directly, the same way the startup seeder does (which also approves the account)
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            user!.IsApproved = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            var result = await users.AddToRoleAsync(user, Roles.Admin);
            Assert.True(result.Succeeded);
        }

        // Log in AFTER granting the role, because roles are written into the cookie at login
        await LoginAsync(client, email);
        return client;
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = Password });
        login.EnsureSuccessStatusCode();
    }
}
