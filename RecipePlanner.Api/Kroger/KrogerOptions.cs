namespace RecipePlanner.Api.Kroger;

public class KrogerOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }       // Azure secret, never in code or appsettings
    public string? RedirectUri { get; set; }        // e.g. https://<your site>/api/kroger/callback; must match the Kroger app exactly
    public string BaseUrl { get; set; } = "https://api.kroger.com";


    public const string UserScopes = "cart.basic:write product.compact profile.compact";
    public const string AppScope = "product.compact";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(RedirectUri);
}
