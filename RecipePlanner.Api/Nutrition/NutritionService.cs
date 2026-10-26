using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Api.Nutrition;

// One USDA lookup per ingredient name, ever: results (including "no match") are stored in the database
// and shared by every user, so USDA's hourly limit is never a concern after the first few recipes.
public class IngredientNutrition
{
    public int Id { get; set; }
    public required string NameKey { get; set; }      // lower-case ingredient name
    public int? FdcId { get; set; }                    // null = USDA had no match
    public string? FactsJson { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public sealed class NutritionService(AppDbContext db, IFoodSource source, TimeProvider clock, ILogger<NutritionService> logger)
{
    public const int MaxLookupsPerRequest = 30;
    private static readonly TimeSpan RetryMissesAfter = TimeSpan.FromDays(30);

    public bool IsAvailable => source.IsAvailable;

    public async Task<Dictionary<string, FoodFacts?>> FactsForAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var keys = names.Select(n => n.Trim().ToLowerInvariant()).Where(n => n.Length > 0).Distinct().ToList();
        var cached = await db.IngredientNutrition.Where(x => keys.Contains(x.NameKey)).ToDictionaryAsync(x => x.NameKey, ct);
        var result = new Dictionary<string, FoodFacts?>();
        var now = clock.GetUtcNow();
        var lookups = 0;

        foreach (var key in keys)
        {
            if (cached.TryGetValue(key, out var row) && (row.FdcId is not null || now - row.FetchedAt < RetryMissesAfter))
            {
                result[key] = row.FactsJson is null ? null : JsonSerializer.Deserialize<FoodFacts>(row.FactsJson);
                continue;
            }
            if (lookups++ >= MaxLookupsPerRequest) { result[key] = null; continue; }

            FoodFacts? facts;
            try { facts = await source.FindAsync(key, ct); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Nutrition lookup failed");
                result[key] = null;
                continue; // not stored, so it's tried again next time
            }

            row ??= new IngredientNutrition { NameKey = key };
            row.FdcId = facts?.FdcId;
            row.FactsJson = facts is null ? null : JsonSerializer.Serialize(facts);
            row.FetchedAt = now;
            if (row.Id == 0) db.IngredientNutrition.Add(row);
            result[key] = facts;
        }
        await db.SaveChangesAsync(ct);
        return result;
    }
}
