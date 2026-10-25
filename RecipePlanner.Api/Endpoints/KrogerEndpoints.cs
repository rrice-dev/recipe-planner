using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Kroger;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

public static partial class KrogerEndpoints
{
    public const string StateCookie = "rp_kroger_oauth";
    public const int MaxMatchItems = 40;
    public const int MaxCartItems = 50;
    public const int MaxPackages = 25;
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    [GeneratedRegex(@"^\d{5}$")] private static partial Regex Zip();
    [GeneratedRegex(@"^[A-Za-z0-9]{1,20}$")] private static partial Regex LocationIdFormat();
    [GeneratedRegex(@"^\d{1,14}$")] private static partial Regex UpcFormat();

    private record OAuthState(string State, string Verifier, string UserId, DateTimeOffset ExpiresAt);

    public static void MapKrogerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/kroger").RequireAuthorization();

        group.MapGet("/status", async (ClaimsPrincipal user, AppDbContext db, IOptions<KrogerOptions> options) =>
        {
            var c = await db.KrogerConnections.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == user.UserId());
            return Results.Ok(new KrogerStatusResponse(options.Value.IsConfigured, c is not null, c?.LocationId, c?.LocationName));
        });

        // Step 1: send the user to Kroger's own sign-in page.
        // This is a normal page visit (not an API call), so the browser follows the redirect.
        group.MapGet("/connect", (HttpContext http, ClaimsPrincipal user, KrogerClient kroger, IOptions<KrogerOptions> options,
            IDataProtectionProvider protection) =>
        {
            if (!options.Value.IsConfigured) return Results.Redirect("/shopping-list?kroger=unavailable");

            // state: proves the user who comes back is the one we sent (stops login-CSRF)
            // PKCE verifier: proves whoever trades in the code is us, even if the code leaks
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            var payload = JsonSerializer.Serialize(new OAuthState(state, verifier, user.UserId(), DateTimeOffset.UtcNow.Add(StateLifetime)));
            http.Response.Cookies.Append(StateCookie, StateProtector(protection).Protect(payload), new CookieOptions
            {
                HttpOnly = true,
                Secure = http.Request.IsHttps,
                // Lax, not Strict: the browser must send it when Kroger redirects back, which is a
                // cross-site navigation. That's also why the callback can't rely on the login cookie.
                SameSite = SameSiteMode.Lax,
                Path = "/api/kroger/callback",
                MaxAge = StateLifetime
            });
            return Results.Redirect(kroger.AuthorizeUrl(state, challenge));
        });

        // Step 2: Kroger sends the user back here with a one-time code
        app.MapGet("/api/kroger/callback", async (string? code, string? state, string? error, HttpContext http,
            AppDbContext db, KrogerClient kroger, KrogerTokens tokens, IDataProtectionProvider protection,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var cookie = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/kroger/callback" });

            if (error is not null || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || cookie is null)
                return Results.Redirect("/shopping-list?kroger=cancelled");

            OAuthState? saved;
            try { saved = JsonSerializer.Deserialize<OAuthState>(StateProtector(protection).Unprotect(cookie)); }
            catch (CryptographicException) { saved = null; }   // tampered or from an old key

            if (saved is null || saved.ExpiresAt < DateTimeOffset.UtcNow ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(saved.State), Encoding.ASCII.GetBytes(state)))
                return Results.Redirect("/shopping-list?kroger=error");

            if (!await db.Users.AnyAsync(u => u.Id == saved.UserId, ct))
                return Results.Redirect("/shopping-list?kroger=error");

            KrogerToken token;
            try { token = await kroger.ExchangeCodeAsync(code, saved.Verifier, ct); }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException)
            {
                loggerFactory.CreateLogger("Kroger").LogWarning(ex, "Kroger code exchange failed");
                return Results.Redirect("/shopping-list?kroger=error");
            }
            if (token.RefreshToken is null) return Results.Redirect("/shopping-list?kroger=error");

            var connection = await db.KrogerConnections.FindAsync([saved.UserId], ct);
            if (connection is null)
            {
                connection = new KrogerConnection { UserId = saved.UserId, EncryptedRefreshToken = "", ConnectedAt = DateTimeOffset.UtcNow };
                db.KrogerConnections.Add(connection);
            }
            tokens.Store(connection, token);
            connection.ConnectedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Redirect("/shopping-list?kroger=connected");
        }).AllowAnonymous();   // the login cookie is SameSite=Strict, so it isn't sent here; the signed state cookie identifies the user

        group.MapDelete("/connection", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var c = await db.KrogerConnections.FindAsync(user.UserId());
            if (c is not null)
            {
                db.KrogerConnections.Remove(c);   // tokens are gone from our side immediately
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        // Store search uses the app's own token, so it works before connecting too
        group.MapGet("/locations", async (string? zip, KrogerClient kroger, IOptions<KrogerOptions> options, CancellationToken ct) =>
        {
            if (!options.Value.IsConfigured) return Unavailable();
            if (zip is null || !Zip().IsMatch(zip))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Zip"] = ["Enter a 5-digit ZIP code."] });
            try { return Results.Ok(await kroger.SearchLocationsAsync(zip, ct)); }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException) { return KrogerDown(); }
        }).RequireRateLimiting(RateLimiting.KrogerPolicy);

        group.MapPut("/store", async (KrogerStoreRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (!LocationIdFormat().IsMatch(request.LocationId ?? "") || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Store"] = ["Pick a store from the list."] });
            var c = await db.KrogerConnections.FindAsync(user.UserId());
            if (c is null) return NotConnected();
            c.LocationId = request.LocationId;
            c.LocationName = request.Name.Trim();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Step 3: suggest Kroger products for each unchecked item on the list
        group.MapPost("/match", async (ClaimsPrincipal user, AppDbContext db, KrogerClient kroger, CancellationToken ct) =>
        {
            var userId = user.UserId();
            var c = await db.KrogerConnections.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
            if (c is null) return NotConnected();
            if (c.LocationId is null)
                return Results.Problem("Pick your Kroger store first.", statusCode: StatusCodes.Status409Conflict);

            var items = await db.GroceryItems.AsNoTracking()
                .Where(g => g.OwnerId == userId && !g.Checked)
                .OrderBy(g => g.Name).Take(MaxMatchItems).ToListAsync(ct);
            var keys = items.Select(i => i.Name.ToLowerInvariant()).ToList();
            var remembered = await db.KrogerProductChoices.AsNoTracking()
                .Where(k => k.UserId == userId && keys.Contains(k.ItemKey)).ToDictionaryAsync(k => k.ItemKey, ct);

            using var gate = new SemaphoreSlim(4);   // be polite to Kroger: at most 4 searches at once
            try
            {
                var matches = await Task.WhenAll(items.Select(async item =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        var found = await kroger.SearchProductsAsync(item.Name, c.LocationId, ct);
                        var options = found.Select(p => new KrogerProductOption(p.Upc, p.Description, p.Brand, p.Size, p.Price, p.PromoPrice, p.ImageUrl)).ToList();
                        string? selected = options.FirstOrDefault()?.Upc;
                        if (remembered.TryGetValue(item.Name.ToLowerInvariant(), out var choice))
                        {
                            if (!options.Any(o => o.Upc == choice.Upc))
                                options.Insert(0, new KrogerProductOption(choice.Upc, choice.Description, null, null, null, null, null));
                            selected = choice.Upc;
                        }
                        var amount = string.Join(" ", new[] { item.Quantity?.ToString("0.##"), item.Unit == "whole" ? null : item.Unit }.Where(s => s is not null));
                        return new KrogerMatch(item.Id, item.Name, amount.Length == 0 ? null : amount, options, selected);
                    }
                    finally { gate.Release(); }
                }));
                return Results.Ok(matches);
            }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException) { return KrogerDown(); }
        }).RequireRateLimiting(RateLimiting.KrogerPolicy);

        // Step 4: put the chosen products in the user's Kroger cart
        group.MapPost("/cart", async (KrogerCartRequest request, ClaimsPrincipal user, AppDbContext db, KrogerClient kroger,
            KrogerTokens tokens, CancellationToken ct) =>
        {
            var modality = request.Modality?.ToUpperInvariant();
            var errors = new Dictionary<string, string[]>();
            if (modality is not ("PICKUP" or "DELIVERY")) errors["Modality"] = ["Choose pickup or delivery."];
            if (request.Items is null || request.Items.Count is 0 or > MaxCartItems) errors["Items"] = [$"Send between 1 and {MaxCartItems} items."];
            else if (request.Items.Any(i => !UpcFormat().IsMatch(i.Upc ?? "") || i.Quantity is < 1 or > MaxPackages || string.IsNullOrWhiteSpace(i.Description)))
                errors["Items"] = [$"Each item needs a product and 1 to {MaxPackages} packages."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var userId = user.UserId();
            var ids = request.Items!.Select(i => i.GroceryItemId).Distinct().ToList();
            var groceries = await db.GroceryItems.Where(g => g.OwnerId == userId && ids.Contains(g.Id)).ToListAsync(ct);
            if (groceries.Count != ids.Count) return Results.NotFound();   // someone else's item, or a deleted one

            var connection = await db.KrogerConnections.FindAsync([userId], ct);
            if (connection is null) return NotConnected();
            var accessToken = await tokens.GetAccessTokenAsync(db, connection, ct);
            if (accessToken is null) return NotConnected();

            // Same product picked for two lines: one cart entry with the packages added up
            var lines = request.Items!.GroupBy(i => i.Upc)
                .Select(g => new KrogerCartLine(g.Key, Math.Min(MaxPackages, g.Sum(i => i.Quantity)))).ToList();
            try { await kroger.AddToCartAsync(accessToken, lines, modality!, ct); }
            catch (KrogerException ex) when (ex.Status is 401 or 403) { return NotConnected(); }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException) { return KrogerDown(); }

            // Remember each choice for next time, and tick the items off the list (they're in the cart now)
            var now = DateTimeOffset.UtcNow;
            foreach (var item in request.Items!)
            {
                var grocery = groceries.First(g => g.Id == item.GroceryItemId);
                var key = grocery.Name.ToLowerInvariant();
                var choice = await db.KrogerProductChoices.FindAsync([userId, key], ct);
                if (choice is null)
                    db.KrogerProductChoices.Add(new KrogerProductChoice
                    {
                        UserId = userId, ItemKey = key, Upc = item.Upc, Description = Shorten(item.Description, 200), UpdatedAt = now
                    });
                else
                {
                    choice.Upc = item.Upc;
                    choice.Description = Shorten(item.Description, 200);
                    choice.UpdatedAt = now;
                }
                if (request.MarkChecked) grocery.Checked = true;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new KrogerCartResult(lines.Count));
        }).RequireRateLimiting(RateLimiting.KrogerPolicy);
    }

    private static IDataProtector StateProtector(IDataProtectionProvider p) => p.CreateProtector("RecipePlanner.Kroger.OAuthState.v1");

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Shorten(string s, int max) { s = s.Trim(); return s.Length <= max ? s : s[..max]; }

    private static IResult NotConnected() =>
        Results.Problem("Connect your Kroger account first.", statusCode: StatusCodes.Status409Conflict, title: "kroger_not_connected");

    private static IResult Unavailable() =>
        Results.Problem("Kroger isn't set up on this site yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult KrogerDown() =>
        Results.Problem("Kroger isn't responding right now. Try again in a minute.", statusCode: StatusCodes.Status502BadGateway);
}
