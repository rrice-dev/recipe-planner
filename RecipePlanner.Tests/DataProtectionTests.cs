using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class DataProtectionTests
{
    [Fact]
    public async Task Login_survives_an_api_restart()
    {
        var database = Guid.NewGuid().ToString();
        string cookie;

        // First "container": register and log in, and keep the session cookie it hands out
        using (var first = new ApiFactory { DatabaseName = database })
        {
            var client = first.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var credentials = new { email = "restart@test.local", password = TestAuth.Password };
            (await client.PostAsJsonAsync("/api/auth/register", credentials)).EnsureSuccessStatusCode();
            await TestAuth.ApproveAsync(first, credentials.email);
            var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", credentials);
            login.EnsureSuccessStatusCode();
            cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("rp_session=")).Split(';')[0];

            // The key must be in the database. Outside a container ASP.NET would otherwise quietly fall
            // back to a folder in the home directory, which makes a restart test pass on a laptop
            // while still failing in Azure, where that folder is wiped with every new container.
            using var scope = first.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEmpty(db.DataProtectionKeys.ToList());
        }

        // A brand-new "container" on the same database must still accept that cookie.
        // Before this fix, it generated new keys, couldn't read the cookie, and returned 401.
        using var second = new ApiFactory { DatabaseName = database };
        var restarted = second.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/manage/info");
        request.Headers.Add("Cookie", cookie);

        var response = await restarted.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
