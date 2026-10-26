using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RecipePlanner.Api.Nutrition;

public class NutritionOptions
{
    /// <summary>Free key from https://api.data.gov/signup (secret: Nutrition__UsdaApiKey)</summary>
    public string? UsdaApiKey { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(UsdaApiKey);
}

/// <summary>Nutrients per 100 g, and how much common portions weigh</summary>
public record FoodFacts(int FdcId, string Description, NutrientsPer100g Per100g, List<Portion> Portions);

public record NutrientsPer100g(double Calories, double Protein, double Carbs, double Fat, double Fiber, double Sugar, double SodiumMg);

/// <param name="Unit">"cup", "tbsp", "clove", "medium"... as USDA describes it</param>
public record Portion(string Unit, double Amount, double Grams);

public interface IFoodSource
{
    bool IsAvailable { get; }
    /// <summary>Best match for an ingredient name, or null when USDA has nothing close</summary>
    Task<FoodFacts?> FindAsync(string ingredient, CancellationToken ct);
}

public sealed class DisabledFoodSource : IFoodSource
{
    public bool IsAvailable => false;
    public Task<FoodFacts?> FindAsync(string ingredient, CancellationToken ct) => Task.FromResult<FoodFacts?>(null);
}

// USDA FoodData Central: https://fdc.nal.usda.gov/api-guide
// Only the "SR Legacy" and "Foundation" datasets are used: lab-measured generic foods, rather than
// branded products, which suit recipe ingredients best.
public sealed class FoodDataCentralClient(HttpClient http, IOptions<NutritionOptions> options, ILogger<FoodDataCentralClient> logger) : IFoodSource
{
    // USDA nutrient numbers
    private const string Energy = "208", Protein = "203", Fat = "204", Carbs = "205", Fiber = "291", Sugar = "269", Sodium = "307";

    public bool IsAvailable => true;

    public async Task<FoodFacts?> FindAsync(string ingredient, CancellationToken ct)
    {
        var key = options.Value.UsdaApiKey!;
        var search = await http.PostAsJsonAsync($"v1/foods/search?api_key={Uri.EscapeDataString(key)}",
            new { query = ingredient, dataType = new[] { "Foundation", "SR Legacy" }, pageSize = 5 }, ct);
        if (!search.IsSuccessStatusCode)
        {
            logger.LogWarning("FoodData Central search returned {Status}", (int)search.StatusCode); // never log the URL: it holds the key
            return null;
        }
        var results = await search.Content.ReadFromJsonAsync<SearchResponse>(ct);
        var best = results?.Foods?.FirstOrDefault();
        if (best is null) return null;

        var details = await http.GetAsync($"v1/food/{best.FdcId}?api_key={Uri.EscapeDataString(key)}", ct);
        if (!details.IsSuccessStatusCode)
        {
            logger.LogWarning("FoodData Central details returned {Status}", (int)details.StatusCode);
            return null;
        }
        return Parse(await details.Content.ReadFromJsonAsync<JsonElement>(ct));
    }

    /// <summary>Reads USDA's food details format (public so tests can feed it sample responses)</summary>
    public static FoodFacts? Parse(JsonElement food)
    {
        if (!food.TryGetProperty("fdcId", out var idEl) || !idEl.TryGetInt32(out var fdcId)) return null;
        var values = new Dictionary<string, double>();
        if (food.TryGetProperty("foodNutrients", out var nutrients))
            foreach (var n in nutrients.EnumerateArray())
            {
                // Details responses nest the nutrient; search responses are flat
                var number = n.TryGetProperty("nutrient", out var inner) && inner.TryGetProperty("number", out var num) ? num.GetString()
                    : n.TryGetProperty("nutrientNumber", out var flat) ? flat.GetString() : null;
                var amount = n.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble()
                    : n.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;
                // Foundation foods list energy in kJ too (number 268); 208 is kcal
                if (number is not null && amount is not null) values.TryAdd(number, amount.Value);
            }

        var portions = new List<Portion>();
        if (food.TryGetProperty("foodPortions", out var ps))
            foreach (var p in ps.EnumerateArray())
            {
                var grams = p.TryGetProperty("gramWeight", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetDouble() : 0;
                var amount = p.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number && am.GetDouble() > 0 ? am.GetDouble() : 1;
                // SR Legacy puts the unit in "modifier" ("cup, chopped"); Foundation uses measureUnit.name
                var unitName = p.TryGetProperty("measureUnit", out var mu) && mu.TryGetProperty("name", out var mn) ? mn.GetString() : null;
                if (unitName is null or "undetermined")
                    unitName = p.TryGetProperty("modifier", out var mod) ? mod.GetString() : p.TryGetProperty("portionDescription", out var pd) ? pd.GetString() : null;
                if (grams > 0 && !string.IsNullOrWhiteSpace(unitName)) portions.Add(new Portion(unitName.Trim().ToLowerInvariant(), amount, grams));
            }

        double V(string k) => values.GetValueOrDefault(k);
        return new FoodFacts(fdcId, food.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            new NutrientsPer100g(V(Energy), V(Protein), V(Carbs), V(Fat), V(Fiber), V(Sugar), V(Sodium)), portions);
    }

    private record SearchResponse([property: JsonPropertyName("foods")] List<SearchFood>? Foods);
    private record SearchFood([property: JsonPropertyName("fdcId")] int FdcId, [property: JsonPropertyName("description")] string? Description);
}
