using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace RecipePlanner.Tests;

// Stands in for api.kroger.com in tests and records every request
public class FakeKroger : HttpMessageHandler
{
    public record Call(string Method, string Path, string Query, string? Auth, string Body);
    public List<Call> Calls { get; } = [];
    public int AccessTokenLifetime { get; set; } = 1800;
    public bool RejectRefresh { get; set; }
    public HttpStatusCode CartStatus { get; set; } = HttpStatusCode.NoContent;
    /// <summary>Answer store searches with something that isn't JSON, like a broken gateway would</summary>
    public bool GarbledLocations { get; set; }
    private int _tokenCounter;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var path = request.RequestUri!.AbsolutePath;
        Calls.Add(new Call(request.Method.Method, path, request.RequestUri.Query, request.Headers.Authorization?.ToString(), body));

        if (path == "/v1/connect/oauth2/token")
        {
            var form = HttpUtility.ParseQueryString(body);
            if (form["grant_type"] == "refresh_token" && RejectRefresh)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
            var n = Interlocked.Increment(ref _tokenCounter);
            return Json(new
            {
                access_token = $"ACCESS-{form["grant_type"]}-{n}",
                refresh_token = form["grant_type"] == "client_credentials" ? null : $"REFRESH-{n}",
                expires_in = AccessTokenLifetime
            });
        }
        if (path == "/v1/locations" && GarbledLocations)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "application/json") };
        if (path == "/v1/locations")
            return Json(new
            {
                data = new[]
                {
                    new { locationId = "01400943", name = "Kroger Montgomery", chain = "KROGER",
                          address = new { addressLine1 = "12111 Montgomery Rd", city = "Cincinnati", state = "OH", zipCode = "45249" } }
                }
            });
        if (path == "/v1/products")
        {
            var term = HttpUtility.ParseQueryString(request.RequestUri.Query)["filter.term"];
            return Json(new
            {
                data = new[]
                {
                    Product("0001111041700", $"Kroger® {term}", "Kroger", "1 lb", 3.49m),
                    Product("0001111099999", $"Simple Truth Organic™ {term}", "Simple Truth", "16 oz", 5.99m)
                }
            });
        }
        if (path == "/v1/cart/add") return new HttpResponseMessage(CartStatus);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static object Product(string upc, string description, string brand, string size, decimal price) => new
    {
        upc, description, brand,
        items = new[] { new { size, price = new { regular = price, promo = 0 } } },
        images = new[] { new { featured = true, perspective = "front", sizes = new[] { new { size = "thumbnail", url = $"https://www.kroger.com/product/images/thumbnail/front/{upc}" } } } }
    };

    private static HttpResponseMessage Json(object o) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };
}
