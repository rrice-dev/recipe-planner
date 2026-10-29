using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Tests;

// Sign-ups are invite-only in practice: anyone can register, but the account can't sign in until an admin approves it
public class AccountApprovalTests
{
    private record PendingUser(string Id, string Email, bool EmailConfirmed, bool IsApproved);

    private static async Task Register(HttpClient client, string email) =>
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = TestAuth.Password })).EnsureSuccessStatusCode();

    private static Task<HttpResponseMessage> Login(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestAuth.Password });

    [Fact]
    public async Task A_new_account_cannot_sign_in_until_an_admin_approves_it()
    {
        using var factory = new ApiFactory();
        var admin = await factory.CreateAdminClientAsync();
        var email = $"new-{Guid.NewGuid():N}@test.local";
        var newcomer = factory.CreateClient();
        await Register(newcomer, email);

        var refused = await Login(newcomer, email);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("NotAllowed", (await refused.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        Assert.Equal(HttpStatusCode.Unauthorized, (await newcomer.GetAsync("/api/recipes")).StatusCode);

        var waiting = (await admin.GetFromJsonAsync<List<PendingUser>>("/api/admin/users"))!.Single(u => u.Email == email);
        Assert.False(waiting.IsApproved);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{waiting.Id}/approve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(newcomer, email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await newcomer.GetAsync("/api/recipes")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_says_nothing_about_whether_the_account_is_waiting()
    {
        using var factory = new ApiFactory();
        var email = $"wait-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();
        await Register(client, email);

        var wrong = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = "not-the-password" });
        var nobody = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email = "nobody@test.local", password = "not-the-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal((await nobody.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail,
                     (await wrong.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Only_admins_can_see_or_approve_accounts()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateUserClientAsync();
        var email = $"other-{Guid.NewGuid():N}@test.local";
        await Register(factory.CreateClient(), email);
        string id;
        using (var scope = factory.Services.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email))!.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/admin/users/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/admin/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Declining_deletes_a_waiting_account_but_never_an_approved_one()
    {
        using var factory = new ApiFactory();
        var admin = await factory.CreateAdminClientAsync();
        var approvedEmail = $"ok-{Guid.NewGuid():N}@test.local";
        await factory.CreateUserClientAsync(approvedEmail);
        var waitingEmail = $"no-{Guid.NewGuid():N}@test.local";
        await Register(factory.CreateClient(), waitingEmail);

        var all = (await admin.GetFromJsonAsync<List<PendingUser>>("/api/admin/users"))!;
        var approved = all.Single(u => u.Email == approvedEmail);
        var waiting = all.Single(u => u.Email == waitingEmail);
        // Waiting accounts are listed first
        Assert.Equal(waitingEmail, all[0].Email);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/users/{approved.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{waiting.Id}")).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<PendingUser>>("/api/admin/users"))!, u => u.Email == waitingEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/users/{waiting.Id}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Approval_can_be_turned_off_for_local_development()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.UseSetting("Registration:RequireApproval", "false"));
        var client = factory.CreateClient();
        var email = $"dev-{Guid.NewGuid():N}@test.local";
        await Register(client, email);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, email)).StatusCode);
    }

    [Fact]
    public async Task The_sign_up_page_can_say_accounts_need_approval()
    {
        using var factory = new ApiFactory();
        var features = await factory.CreateClient().GetFromJsonAsync<Dictionary<string, object>>("/api/features");
        Assert.Equal("True", features!["signUpsNeedApproval"].ToString());
    }

    [Fact]
    public async Task The_configured_admin_is_approved_when_promoted()
    {
        var name = Guid.NewGuid().ToString();
        var email = $"owner-{Guid.NewGuid():N}@test.local";
        using (var first = new ApiFactory { DatabaseName = name })
        {
            await Register(first.CreateClient(), email);
            using var scope = first.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        // Restart with Admin:Email set, the way production names its admin
        using var restarted = new ApiFactory { DatabaseName = name };
        using var withAdmin = restarted.WithWebHostBuilder(b => b.UseSetting("Admin:Email", email));
        Assert.Equal(HttpStatusCode.OK, (await Login(withAdmin.CreateClient(), email)).StatusCode);
    }
}
