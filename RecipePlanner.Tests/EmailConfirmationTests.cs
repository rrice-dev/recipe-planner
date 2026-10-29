using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Tests;

public partial class EmailConfirmationTests
{
    [GeneratedRegex(@"https://recipes\.example\.com/confirm-email\?[^\s""<]+")]
    private static partial Regex ConfirmLink();

    private record Info(string Email, bool IsEmailConfirmed);

    private static Uri LinkIn(FakeEmail mail, string to)
    {
        var sent = mail.Sent.Last(m => m.To == to && m.Subject.Contains("Confirm"));
        return new Uri(ConfirmLink().Match(sent.Text).Value);
    }

    // What the Angular confirm page does: hand the link's query to Identity's endpoint
    private static Task<HttpResponseMessage> Confirm(HttpClient client, Uri link) =>
        client.GetAsync("/api/auth/confirmEmail" + link.Query);

    [Fact]
    public async Task Sign_up_emails_a_confirmation_link_on_the_site_that_confirms_the_address()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"new-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);

        Assert.False((await client.GetFromJsonAsync<Info>("/api/auth/manage/info"))!.IsEmailConfirmed);

        var link = LinkIn(mail, email);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(factory.CreateClient(), link)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<Info>("/api/auth/manage/info"))!.IsEmailConfirmed);
    }

    [Fact]
    public async Task The_link_ignores_a_spoofed_host()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"spoof-{Guid.NewGuid():N}@test.local";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
            { Content = JsonContent.Create(new { email, password = TestAuth.Password }) };
        request.Headers.Host = "evil.example";
        request.Headers.Add("X-Forwarded-Host", "evil.example");
        (await factory.CreateClient().SendAsync(request)).EnsureSuccessStatusCode();

        var sent = mail.Sent.Single(m => m.To == email);
        Assert.DoesNotContain("evil.example", sent.Html + sent.Text);
        Assert.StartsWith(FakeEmail.SiteUrl + "/confirm-email?", LinkIn(mail, email).ToString());
    }

    [Fact]
    public async Task A_tampered_link_does_not_confirm()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"tamper-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);

        var link = LinkIn(mail, email);
        var bad = new Uri(Regex.Replace(link.ToString(), "code=[^&]+", "code=bm90LWEtcmVhbC1jb2Rl"));
        Assert.NotEqual(HttpStatusCode.OK, (await Confirm(factory.CreateClient(), bad)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<Info>("/api/auth/manage/info"))!.IsEmailConfirmed);
    }

    [Fact]
    public async Task Resend_emails_a_new_link_that_works()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"resend-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);
        var before = mail.Sent.Count(m => m.To == email);

        (await factory.CreateClient().PostAsJsonAsync("/api/auth/resendConfirmationEmail", new { email })).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, mail.Sent.Count(m => m.To == email));
        Assert.Equal(HttpStatusCode.OK, (await Confirm(factory.CreateClient(), LinkIn(mail, email))).StatusCode);
        Assert.True((await client.GetFromJsonAsync<Info>("/api/auth/manage/info"))!.IsEmailConfirmed);
    }

    // Every endpoint that can call the AI, with a body that would otherwise pass validation
    public static TheoryData<string, object?> AiEndpoints => new()
    {
        { "/api/fridge/suggestions", new FridgeRequest(["eggs"]) },
        { "/api/fridge/recipe", new { dishName = "Omelette", ingredients = new[] { "eggs" } } },
        { "/api/fridge/scan", null },
        { "/api/world/dishes", new { country = "Peru" } },
        { "/api/world/recipe", new { country = "Peru", dishName = "Ceviche" } },
        { "/api/import", new { url = "https://recipes.example.org/pancakes" } },
        { "/api/import/photos", new { photos = new[] { "aGVsbG8=" } } },
        { "/api/import/text", new { text = "Pancakes: 2 eggs, 1 cup flour. Mix and fry until golden." } },
    };

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object? body) =>
        body is null
            ? client.PostAsync(url, new ByteArrayContent(TestImages.Jpeg(64, 64)) { Headers = { ContentType = new("image/jpeg") } })
            : client.PostAsJsonAsync(url, body);

    [Theory]
    [MemberData(nameof(AiEndpoints))]
    public async Task Ai_features_need_a_confirmed_email(string url, object? body)
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail, MealIdeas = new FakeMealIdeas() };
        var email = $"ai-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);

        var blocked = await Post(client, url, body);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(ConfirmedEmail.ProblemTitle, problem!.Title);

        (await Confirm(factory.CreateClient(), LinkIn(mail, email))).EnsureSuccessStatusCode();
        var allowed = await Post(client, url, body);
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
    }

    [Fact]
    public async Task Without_email_set_up_there_is_nothing_to_confirm_so_ai_stays_open()
    {
        using var factory = new ApiFactory { MealIdeas = new FakeMealIdeas() };
        var client = await factory.CreateUserClientAsync();
        var response = await client.PostAsJsonAsync("/api/fridge/suggestions", new FridgeRequest(["eggs"]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
