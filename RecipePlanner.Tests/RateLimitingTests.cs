using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Tests;

public class RateLimitingTests
{
    private static Task<HttpResponseMessage> BadLogin(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email = "nobody@test.local", password = "wrong-password-123" });

    [Fact]
    public async Task Repeated_login_attempts_get_429_with_retry_after()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < RateLimiting.AuthPermitLimit; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await BadLogin(client)).StatusCode);

        var blocked = await BadLogin(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Strict_limit_only_applies_to_auth_calls()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        for (var i = 0; i <= RateLimiting.AuthPermitLimit; i++)
            await BadLogin(client);

        // Browsing techniques is unaffected by exhausting the login limit
        var techniques = await client.GetAsync("/api/techniques");
        Assert.Equal(HttpStatusCode.OK, techniques.StatusCode);
    }
}
