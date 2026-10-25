using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Endpoints;

namespace RecipePlanner.Tests;

public class KrogerTests
{
    private static HttpClient NoRedirects(ApiFactory f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    // Logs in without automatic cookie handling, so tests control exactly which cookies are sent
    private static async Task<string> LoginCookie(ApiFactory factory, string email)
    {
        var client = NoRedirects(factory);
        var creds = new { email, password = TestAuth.Password };
        (await client.PostAsJsonAsync("/api/auth/register", creds)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", creds);
        return login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("rp_session=")).Split(';')[0];
    }

    private static HttpRequestMessage Get(string url, params string[] cookies)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookies.Length > 0) r.Headers.Add("Cookie", string.Join("; ", cookies));
        return r;
    }

    // Runs the whole connect flow and returns a cookie-handling client for that user
    private static async Task<HttpClient> Connected(ApiFactory factory, FakeKroger kroger)
    {
        var email = $"k{Guid.NewGuid():N}@test.local";
        var session = await LoginCookie(factory, email);
        var raw = NoRedirects(factory);

        var connect = await raw.SendAsync(Get("/api/kroger/connect", session));
        var state = HttpUtility.ParseQueryString(connect.Headers.Location!.Query)["state"];
        var stateCookie = connect.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(KrogerEndpoints.StateCookie)).Split(';')[0];

        // Coming back from Kroger: only the state cookie is sent (the login cookie is SameSite=Strict)
        var callback = await raw.SendAsync(Get($"/api/kroger/callback?code=THE-CODE&state={Uri.EscapeDataString(state!)}", stateCookie));
        Assert.Equal("/shopping-list?kroger=connected", callback.Headers.Location!.ToString());

        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestAuth.Password });
        return client;
    }

    private static async Task AddItems(HttpClient client, params string[] names) =>
        (await client.PostAsJsonAsync("/api/shopping-list/items", names.Select(n => new GroceryAddRequest(n, null, null, null)).ToArray()))
            .EnsureSuccessStatusCode();

    [Fact]
    public async Task Connect_redirects_to_Kroger_with_state_PKCE_and_scopes()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var session = await LoginCookie(factory, "a@test.local");

        var response = await NoRedirects(factory).SendAsync(Get("/api/kroger/connect", session));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("/v1/connect/oauth2/authorize", location.AbsolutePath);
        var q = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Contains("cart.basic:write", q["scope"]);
        Assert.Equal(43, q["state"]!.Length);
        Assert.Equal(43, q["code_challenge"]!.Length);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(KrogerEndpoints.StateCookie));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(q["state"]!, cookie);                // the cookie is encrypted, not readable
    }

    [Fact]
    public async Task Callback_sends_the_PKCE_verifier_and_stores_tokens_encrypted()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        await Connected(factory, kroger);

        var exchange = kroger.Calls.Single(c => c.Path == "/v1/connect/oauth2/token" && c.Body.Contains("authorization_code"));
        var form = HttpUtility.ParseQueryString(exchange.Body);
        Assert.Equal("THE-CODE", form["code"]);
        Assert.Equal(43, form["code_verifier"]!.Length);
        Assert.StartsWith("Basic ", exchange.Auth);

        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KrogerConnections.SingleAsync();
        Assert.DoesNotContain("REFRESH-", row.EncryptedRefreshToken);   // never stored in plain text
        Assert.DoesNotContain("ACCESS-", row.EncryptedAccessToken!);
    }

    [Fact]
    public async Task Callback_rejects_a_wrong_state_or_missing_cookie()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var session = await LoginCookie(factory, "b@test.local");
        var raw = NoRedirects(factory);
        var connect = await raw.SendAsync(Get("/api/kroger/connect", session));
        var stateCookie = connect.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(KrogerEndpoints.StateCookie)).Split(';')[0];

        // An attacker's link with their own code and a made-up state
        var forged = await raw.SendAsync(Get("/api/kroger/callback?code=ATTACKER&state=not-the-state", stateCookie));
        Assert.Equal("/shopping-list?kroger=error", forged.Headers.Location!.ToString());

        var noCookie = await raw.SendAsync(Get("/api/kroger/callback?code=ATTACKER&state=x"));
        Assert.Equal("/shopping-list?kroger=cancelled", noCookie.Headers.Location!.ToString());

        var tampered = await raw.SendAsync(Get("/api/kroger/callback?code=ATTACKER&state=x", $"{KrogerEndpoints.StateCookie}=garbage"));
        Assert.Equal("/shopping-list?kroger=error", tampered.Headers.Location!.ToString());

        Assert.DoesNotContain(kroger.Calls, c => c.Body.Contains("ATTACKER"));   // never traded in
    }

    [Fact]
    public async Task Find_store_match_products_and_send_to_cart()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await Connected(factory, kroger);

        var stores = await client.GetFromJsonAsync<List<JsonElement>>("/api/kroger/locations?zip=45249");
        var store = stores!.Single();
        Assert.Equal("01400943", store.GetProperty("locationId").GetString());
        (await client.PutAsJsonAsync("/api/kroger/store", new KrogerStoreRequest("01400943", "Kroger Montgomery"))).EnsureSuccessStatusCode();

        await AddItems(client, "Butter", "Eggs");
        var matches = (await (await client.PostAsync("/api/kroger/match", null)).Content.ReadFromJsonAsync<List<KrogerMatch>>())!;
        Assert.Equal(2, matches.Count);
        var butter = matches.Single(m => m.Name == "Butter");
        Assert.Equal(2, butter.Options.Count);
        Assert.Contains(kroger.Calls, c => c.Path == "/v1/products" && c.Query.Contains("filter.locationId=01400943"));

        var send = await client.PostAsJsonAsync("/api/kroger/cart", new KrogerCartRequest("PICKUP",
        [
            new KrogerCartItem(butter.GroceryItemId, "0001111099999", "Simple Truth Organic™ Butter", 2),
            new KrogerCartItem(matches.Single(m => m.Name == "Eggs").GroceryItemId, "0001111041700", "Kroger® Eggs", 1)
        ]));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var cart = kroger.Calls.Single(c => c.Path == "/v1/cart/add");
        Assert.Equal("PUT", cart.Method);
        Assert.StartsWith("Bearer ACCESS-authorization_code", cart.Auth);      // the user's token, not the app's
        using var body = JsonDocument.Parse(cart.Body);
        var first = body.RootElement.GetProperty("items")[0];
        Assert.Equal("0001111099999", first.GetProperty("upc").GetString());
        Assert.Equal(2, first.GetProperty("quantity").GetInt32());
        Assert.Equal("PICKUP", first.GetProperty("modality").GetString());

        // Sent items are ticked off, and the choice is remembered for next time
        Assert.All(await client.GetFromJsonAsync<List<GroceryItemResponse>>("/api/shopping-list") ?? [], i => Assert.True(i.Checked));
        await AddItems(client, "Butter");
        var again = (await (await client.PostAsync("/api/kroger/match", null)).Content.ReadFromJsonAsync<List<KrogerMatch>>())!;
        Assert.Equal("0001111099999", again.Single().SelectedUpc);
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_and_a_revoked_one_disconnects()
    {
        var kroger = new FakeKroger { AccessTokenLifetime = 0 };   // every access token is already expired
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await Connected(factory, kroger);
        await client.PutAsJsonAsync("/api/kroger/store", new KrogerStoreRequest("01400943", "Kroger Montgomery"));
        await AddItems(client, "Milk");
        var item = (await client.GetFromJsonAsync<List<GroceryItemResponse>>("/api/shopping-list"))!.Single();
        var request = new KrogerCartRequest("DELIVERY", [new KrogerCartItem(item.Id, "0001111041700", "Kroger® Milk", 1)], MarkChecked: false);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/kroger/cart", request)).StatusCode);
        Assert.Contains(kroger.Calls, c => c.Body.Contains("grant_type=refresh_token"));

        kroger.RejectRefresh = true;
        var revoked = await client.PostAsJsonAsync("/api/kroger/cart", request);
        Assert.Equal(HttpStatusCode.Conflict, revoked.StatusCode);
        var status = await client.GetFromJsonAsync<KrogerStatusResponse>("/api/kroger/status");
        Assert.False(status!.Connected);
    }

    [Fact]
    public async Task Cannot_send_someone_elses_items_or_bad_values()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var alice = await Connected(factory, kroger);
        var bob = await Connected(factory, kroger);
        await AddItems(alice, "Bread");
        var aliceItem = (await alice.GetFromJsonAsync<List<GroceryItemResponse>>("/api/shopping-list"))!.Single();

        var steal = await bob.PostAsJsonAsync("/api/kroger/cart",
            new KrogerCartRequest("PICKUP", [new KrogerCartItem(aliceItem.Id, "0001111041700", "Bread", 1)]));
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);

        foreach (var bad in new[]
        {
            new KrogerCartRequest("TELEPORT", [new KrogerCartItem(aliceItem.Id, "0001111041700", "Bread", 1)]),
            new KrogerCartRequest("PICKUP", [new KrogerCartItem(aliceItem.Id, "not-a-upc", "Bread", 1)]),
            new KrogerCartRequest("PICKUP", [new KrogerCartItem(aliceItem.Id, "0001111041700", "Bread", 500)]),
            new KrogerCartRequest("PICKUP", [])
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/kroger/cart", bad)).StatusCode);

        Assert.DoesNotContain(kroger.Calls, c => c.Path == "/v1/cart/add");
    }

    [Fact]
    public async Task Disconnect_removes_tokens_and_requires_login()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await Connected(factory, kroger);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/kroger/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/kroger/connection")).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().KrogerConnections);
    }

    [Theory]
    [InlineData("4524")]
    [InlineData("45249-1234")]
    [InlineData("abcde")]
    public async Task Store_search_needs_a_5_digit_zip(string zip)
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/kroger/locations?zip={zip}")).StatusCode);
    }

    [Fact]
    public async Task Says_when_Kroger_is_not_set_up()
    {
        using var factory = new ApiFactory();   // no Kroger credentials configured
        var client = await factory.CreateUserClientAsync();
        var status = await client.GetFromJsonAsync<KrogerStatusResponse>("/api/kroger/status");
        Assert.False(status!.Available);
    }
}
