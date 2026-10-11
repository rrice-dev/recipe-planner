using System.Net.Http.Json;

namespace RecipePlanner.Tests;

public static class TestAuth
{
    public const string Password = "correct-horse-battery";

    public static async Task<HttpClient> CreateUserClientAsync(this ApiFactory factory, string? email = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient(); // keeps cookies between requests // keeps cookies between requests

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        return client;
    }
}