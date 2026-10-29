using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Api.Kroger;

// Kroger's search results for an ingredient at a store, shared by every user of that store.
// Prices change, so results are refreshed after a few days.
public class IngredientPrice
{
    public int Id { get; set; }
    public required string LocationId { get; set; }
    public required string NameKey { get; set; }
    public required string ProductsJson { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public record PricedProduct(string Upc, string Description, string? Size, decimal Price);

public sealed class PriceService(AppDbContext db, KrogerClient kroger, TimeProvider clock, ILogger<PriceService> logger)
{
    public const int MaxSearchesPerRequest = 25;
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(3);

    /// <summary>The product to price each ingredient with: your usual pick when Kroger still lists it, otherwise the top result</summary>
    public async Task<Dictionary<string, PricedProduct?>> ProductsForAsync(string locationId, string userId, IEnumerable<string> names, CancellationToken ct)
    {
        var keys = names.Select(Key).Where(k => k.Length > 0).Distinct().ToList();
        var now = clock.GetUtcNow();
        var cached = await db.IngredientPrices.Where(p => p.LocationId == locationId && keys.Contains(p.NameKey)).ToDictionaryAsync(p => p.NameKey, ct);
        var usual = await db.KrogerProductChoices.AsNoTracking().Where(c => c.UserId == userId && keys.Contains(c.ItemKey))
            .ToDictionaryAsync(c => c.ItemKey, c => c.Upc, ct);

        var stale = keys.Where(k => !cached.TryGetValue(k, out var row) || now - row.FetchedAt > RefreshAfter).Take(MaxSearchesPerRequest).ToList();
        using var gate = new SemaphoreSlim(4);   // at most 4 Kroger searches at once
        var fresh = await Task.WhenAll(stale.Select(async key =>
        {
            await gate.WaitAsync(ct);
            try { return (key, products: (List<KrogerProduct>?)await kroger.SearchProductsAsync(key, locationId, ct)); }
            catch (Exception ex) when (ex is KrogerException or HttpRequestException)
            {
                logger.LogWarning(ex, "Kroger price search failed");
                return (key, products: null);   // priced from the old copy if there is one, and tried again next time
            }
            finally { gate.Release(); }
        }));

        foreach (var (key, products) in fresh)
        {
            if (products is null) continue;
            var json = JsonSerializer.Serialize(products.Where(p => (p.PromoPrice ?? p.Price) is > 0)
                .Select(p => new PricedProduct(p.Upc, p.Description, p.Size, (p.PromoPrice ?? p.Price)!.Value)).ToList());
            if (cached.TryGetValue(key, out var row)) { row.ProductsJson = json; row.FetchedAt = now; }
            else db.IngredientPrices.Add(cached[key] = new IngredientPrice { LocationId = locationId, NameKey = key, ProductsJson = json, FetchedAt = now });
        }
        await db.SaveCacheAsync(ct); // another request may have just cached the same name (see CacheSaving)

        return keys.ToDictionary(k => k, k =>
        {
            if (!cached.TryGetValue(k, out var row)) return null;
            var options = JsonSerializer.Deserialize<List<PricedProduct>>(row.ProductsJson) ?? [];
            return (usual.TryGetValue(k, out var upc) ? options.FirstOrDefault(o => o.Upc == upc) : null)
                ?? options.FirstOrDefault(o => PriceEstimator.ParseSize(o.Size) is not null)
                ?? options.FirstOrDefault();
        });
    }

    public static string Key(string name) => name.Trim().ToLowerInvariant();
}
