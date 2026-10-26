using System.Globalization;
using System.Text.RegularExpressions;
using RecipePlanner.Api.Nutrition;

namespace RecipePlanner.Api.Kroger;

// Works out what the amount a recipe uses costs, from a store package's size and price:
// 1 cup of flour from a $3.49, 5 lb bag is about 125 g of 2,268 g, so about $0.19.
// Pure logic, no network, so it's easy to test. These are estimates, never exact.
public static partial class PriceEstimator
{
    public enum Measure { Mass, Volume, Count }

    /// <summary>A package size in base units: grams, millilitres, or items</summary>
    public record PackageSize(double Amount, Measure Measure);

    private const double GramsPerOz = 28.3495, GramsPerLb = 453.592, MlPerFlOz = 29.5735;

    private static readonly Dictionary<string, (double Factor, Measure Measure)> SizeUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oz"] = (GramsPerOz, Measure.Mass), ["ounce"] = (GramsPerOz, Measure.Mass), ["ounces"] = (GramsPerOz, Measure.Mass),
        ["lb"] = (GramsPerLb, Measure.Mass), ["lbs"] = (GramsPerLb, Measure.Mass), ["pound"] = (GramsPerLb, Measure.Mass),
        ["g"] = (1, Measure.Mass), ["gram"] = (1, Measure.Mass), ["grams"] = (1, Measure.Mass), ["kg"] = (1000, Measure.Mass),
        ["fl oz"] = (MlPerFlOz, Measure.Volume), ["fl. oz"] = (MlPerFlOz, Measure.Volume), ["floz"] = (MlPerFlOz, Measure.Volume),
        ["gal"] = (3785.41, Measure.Volume), ["gallon"] = (3785.41, Measure.Volume), ["qt"] = (946.353, Measure.Volume),
        ["quart"] = (946.353, Measure.Volume), ["pt"] = (473.176, Measure.Volume), ["pint"] = (473.176, Measure.Volume),
        ["ml"] = (1, Measure.Volume), ["l"] = (1000, Measure.Volume), ["liter"] = (1000, Measure.Volume), ["litre"] = (1000, Measure.Volume),
        ["ct"] = (1, Measure.Count), ["count"] = (1, Measure.Count), ["each"] = (1, Measure.Count), ["ea"] = (1, Measure.Count),
        ["pk"] = (1, Measure.Count), ["pack"] = (1, Measure.Count),
    };

    // Recipe units that mean "one whole package" when the package has no matching size: 1 can, 1 jar...
    private static readonly HashSet<string> PackageUnits = new(StringComparer.OrdinalIgnoreCase)
        { "can", "jar", "bottle", "package", "pkg", "bag", "box", "carton", "container", "block", "stick" };

    private static readonly Dictionary<string, double> RecipeMl = new(StringComparer.OrdinalIgnoreCase)
        { ["tsp"] = 4.929, ["tbsp"] = 14.787, ["cup"] = 236.588, ["fl_oz"] = MlPerFlOz, ["fl oz"] = MlPerFlOz, ["ml"] = 1, ["l"] = 1000, ["pint"] = 473.176, ["quart"] = 946.353 };

    private static readonly Dictionary<string, double> RecipeGrams = new(StringComparer.OrdinalIgnoreCase)
        { ["g"] = 1, ["kg"] = 1000, ["oz"] = GramsPerOz, ["lb"] = GramsPerLb };

    private static readonly HashSet<string> CountUnits = new(StringComparer.OrdinalIgnoreCase)
        { "whole", "each", "piece", "large", "medium", "small", "egg", "eggs", "item" };

    // "2 x 14.5 oz", "16 oz", "1 gal", "12 ct", "1.5 lb", "32 fl oz"
    [GeneratedRegex(@"(?:(?<n>\d+)\s*(?:x|/)\s*)?(?<q>\d+(?:\.\d+)?|\.\d+)\s*(?<u>fl\.?\s*oz|[a-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();

    /// <summary>The first size Kroger lists that has a known unit, preferring weight or volume over a count</summary>
    public static PackageSize? ParseSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return null;
        PackageSize? count = null;
        foreach (Match m in SizePattern().Matches(size))
        {
            var unit = Regex.Replace(m.Groups["u"].Value, @"\s+", " ").Replace("fl. oz", "fl oz");
            if (!SizeUnits.TryGetValue(unit, out var u)) continue;
            var amount = double.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture) * u.Factor;
            if (m.Groups["n"].Success) amount *= int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (amount <= 0) continue;
            if (u.Measure != Measure.Count) return new PackageSize(amount, u.Measure);
            count ??= new PackageSize(amount, Measure.Count);
        }
        return count;
    }

    /// <summary>
    /// What the amount used costs, or null when the recipe's unit can't be matched to the package.
    /// Approximate is true when a weight had to be guessed from a volume (or the other way round).
    /// </summary>
    public static (decimal Cost, bool Approximate)? CostOf(decimal quantity, string unit, decimal packagePrice, string? packageSize, FoodFacts? food)
    {
        if (quantity <= 0 || packagePrice <= 0) return null;
        var u = unit.Trim().ToLowerInvariant();
        var package = ParseSize(packageSize);

        // "1 can" of a 15 oz can is the whole can
        if (PackageUnits.Contains(u) && (package is null || package.Measure != Measure.Count))
            return (Round(packagePrice * quantity), false);
        if (package is null) return null;

        double? needed = null;
        var approximate = false;
        switch (package.Measure)
        {
            case Measure.Mass:
                if (RecipeGrams.TryGetValue(u, out var g)) needed = (double)quantity * g;
                else if (food is not null && NutritionCalculator.ToGrams(quantity, u, food) is { } grams)
                    (needed, approximate) = (grams.Grams, true);
                else if (RecipeMl.TryGetValue(u, out var ml)) (needed, approximate) = ((double)quantity * ml, true); // as water
                break;
            case Measure.Volume:
                if (RecipeMl.TryGetValue(u, out var ml2)) needed = (double)quantity * ml2;
                else if (RecipeGrams.TryGetValue(u, out var g2)) (needed, approximate) = ((double)quantity * g2, true); // as water
                break;
            case Measure.Count:
                if (CountUnits.Contains(u)) needed = (double)quantity;
                // A bunch, head or clove from a 1-count package: one package covers it
                else if (u is "bunch" or "head") needed = (double)quantity;
                break;
        }
        if (needed is null) return null;
        return (Round(packagePrice * (decimal)(needed.Value / package.Amount)), approximate);
    }

    private static decimal Round(decimal d) => Math.Round(d, 2, MidpointRounding.AwayFromZero);
}
