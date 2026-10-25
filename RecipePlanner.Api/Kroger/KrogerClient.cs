using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RecipePlanner.Api.Kroger;

public record KrogerToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
public record KrogerLocation(string LocationId, string Name, string Address);
public record KrogerProduct(string Upc, string Description, string? Brand, string? Size, decimal? Price, decimal? PromoPrice, string? ImageUrl);
public record KrogerCartLine(string Upc, int Quantity);

public class KrogerException(string message, int? status = null) : Exception(message)
{
    public int? Status { get; } = status;
}

// Talks to Kroger's public API. Everything Kroger sends back is treated as untrusted data.
public class KrogerClient(HttpClient http, IOptions<KrogerOptions> options, KrogerAppTokenCache appTokens, ILogger<KrogerClient> logger)
{
    private KrogerOptions O => options.Value;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string AuthorizeUrl(string state, string codeChallenge) =>
        $"{O.BaseUrl.TrimEnd('/')}/v1/connect/oauth2/authorize" +
        $"?scope={Uri.EscapeDataString(KrogerOptions.UserScopes)}" +
        $"&response_type=code" +
        $"&client_id={Uri.EscapeDataString(O.ClientId!)}" +
        $"&redirect_uri={Uri.EscapeDataString(O.RedirectUri!)}" +
        $"&state={Uri.EscapeDataString(state)}" +
        $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
        $"&code_challenge_method=S256";

    public Task<KrogerToken> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct) =>
        TokenAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = O.RedirectUri!,
            ["code_verifier"] = codeVerifier
        }, ct);

    public Task<KrogerToken> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    public async Task<List<KrogerLocation>> SearchLocationsAsync(string zip, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"v1/locations?filter.zipCode.near={Uri.EscapeDataString(zip)}&filter.radiusInMiles=15&filter.limit=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AppTokenAsync(ct));
        var doc = await SendAsync<LocationsResponse>(request, ct);
        return (doc.Data ?? []).Where(l => !string.IsNullOrWhiteSpace(l.LocationId)).Select(l => new KrogerLocation(
            l.LocationId!,
            Short(l.Name ?? l.Chain ?? "Kroger", 80),
            Short(string.Join(", ", new[] { l.Address?.AddressLine1, l.Address?.City, l.Address?.State }.Where(s => !string.IsNullOrWhiteSpace(s))), 120)))
            .ToList();
    }

    public async Task<List<KrogerProduct>> SearchProductsAsync(string term, string locationId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"v1/products?filter.term={Uri.EscapeDataString(term)}&filter.locationId={Uri.EscapeDataString(locationId)}&filter.limit=6");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AppTokenAsync(ct));
        var doc = await SendAsync<ProductsResponse>(request, ct);
        return (doc.Data ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Upc) && !string.IsNullOrWhiteSpace(p.Description))
            .Select(p =>
            {
                var item = p.Items?.FirstOrDefault();
                var image = p.Images?.FirstOrDefault(i => i.Featured == true) ?? p.Images?.FirstOrDefault();
                var url = image?.Sizes?.FirstOrDefault(s => s.Size == "thumbnail")?.Url ?? image?.Sizes?.FirstOrDefault()?.Url;
                return new KrogerProduct(
                    p.Upc!, Short(p.Description!, 120), p.Brand is null ? null : Short(p.Brand, 60),
                    item?.Size is null ? null : Short(item.Size, 40),
                    item?.Price?.Regular is > 0 ? item.Price.Regular : null,
                    item?.Price?.Promo is > 0 ? item.Price.Promo : null,
                    // Only show images Kroger serves over https
                    url is not null && url.StartsWith("https://", StringComparison.Ordinal) ? url : null);
            })
            .ToList();
    }

    public async Task AddToCartAsync(string userAccessToken, IEnumerable<KrogerCartLine> lines, string modality, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "v1/cart/add")
        {
            // A plain, fixed-length body: JsonContent would stream it "chunked", which some API gateways reject
            Content = new StringContent(
                JsonSerializer.Serialize(new { items = lines.Select(l => new { upc = l.Upc, quantity = l.Quantity, modality }) }),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Kroger cart add returned {Status}", (int)response.StatusCode);
            throw new KrogerException("Kroger didn't accept the cart update.", (int)response.StatusCode);
        }
    }

    // ---------- plumbing ----------

    private async Task<string> AppTokenAsync(CancellationToken ct) =>
        await appTokens.GetAsync(async () =>
            await TokenAsync(new() { ["grant_type"] = "client_credentials", ["scope"] = KrogerOptions.AppScope }, ct));

    private async Task<KrogerToken> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/connect/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{O.ClientId}:{O.ClientSecret}")));
        var token = await SendAsync<TokenResponse>(request, ct);
        if (string.IsNullOrWhiteSpace(token.AccessToken)) throw new KrogerException("Kroger didn't return a token.");
        // Treat it as expiring a minute early so it never runs out mid-request
        return new KrogerToken(token.AccessToken, token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn ?? 1800) - 60));
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Log the status only: bodies from token calls can contain codes or tokens
            logger.LogWarning("Kroger {Path} returned {Status}", request.RequestUri?.AbsolutePath, (int)response.StatusCode);
            throw new KrogerException("Kroger request failed.", (int)response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new KrogerException("Empty response from Kroger.");
    }

    private static string Short(string s, int max) { s = s.Trim(); return s.Length <= max ? s : s[..(max - 1)] + "…"; }

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);
    private record LocationsResponse(List<LocationData>? Data);
    private record LocationData(string? LocationId, string? Name, string? Chain, AddressData? Address);
    private record AddressData(string? AddressLine1, string? City, string? State, string? ZipCode);
    private record ProductsResponse(List<ProductData>? Data);
    private record ProductData(string? Upc, string? Description, string? Brand, List<ItemData>? Items, List<ImageData>? Images);
    private record ItemData(string? Size, PriceData? Price);
    private record PriceData(decimal? Regular, decimal? Promo);
    private record ImageData(bool? Featured, string? Perspective, List<ImageSize>? Sizes);
    private record ImageSize(string? Size, string? Url);
}

// The app-level token (for searching stores and products) is shared by all users, so cache it
public class KrogerAppTokenCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private KrogerToken? _token;

    public async Task<string> GetAsync(Func<Task<KrogerToken>> fetch)
    {
        if (_token is { } t && t.ExpiresAt > DateTimeOffset.UtcNow) return t.AccessToken;
        await _lock.WaitAsync();
        try
        {
            if (_token is null || _token.ExpiresAt <= DateTimeOffset.UtcNow) _token = await fetch();
            return _token.AccessToken;
        }
        finally { _lock.Release(); }
    }
}
