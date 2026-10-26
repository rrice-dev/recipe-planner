namespace RecipePlanner.Api.Nutrition;

// Converts "2 cup chopped onion" into grams using the USDA portion weights, then adds up the nutrients.
// These are estimates: a cup of something chopped varies, and the USDA match is the closest generic food.
public static class NutritionCalculator
{
    private const double GramsPerOz = 28.3495, GramsPerLb = 453.592;
    private static readonly Dictionary<string, double> MlPer = new() { ["tsp"] = 4.929, ["tbsp"] = 14.787, ["cup"] = 236.588, ["fl_oz"] = 29.574, ["ml"] = 1, ["l"] = 1000 };

    // How each app unit might appear in USDA's portion names
    private static readonly Dictionary<string, string[]> PortionWords = new()
    {
        ["cup"] = ["cup"], ["tbsp"] = ["tbsp", "tablespoon"], ["tsp"] = ["tsp", "teaspoon"],
        ["clove"] = ["clove"], ["slice"] = ["slice"], ["stalk"] = ["stalk"], ["head"] = ["head"],
        ["can"] = ["can"], ["sprig"] = ["sprig"], ["stick"] = ["stick"], ["inch"] = ["inch"],
        ["whole"] = ["medium", "large", "whole", "small", "each", "item", "fruit", "piece"],
        ["piece"] = ["piece", "medium", "each"], ["bunch"] = ["bunch"], ["package"] = ["package", "pkg"],
    };

    /// <summary>Grams for an amount, or null when the unit can't be turned into a weight</summary>
    public static (double Grams, bool Approximate)? ToGrams(decimal quantity, string unit, FoodFacts food)
    {
        var q = (double)quantity;
        switch (unit.ToLowerInvariant())
        {
            case "g": return (q, false);
            case "kg": return (q * 1000, false);
            case "oz": return (q * GramsPerOz, false);
            case "lb": return (q * GramsPerLb, false);
            case "pinch": return (q * 0.36, true);   // about 1/16 tsp of salt
            case "dash": return (q * 0.6, true);
        }

        // A matching USDA portion: "1 cup, chopped = 160 g"
        var u = unit.ToLowerInvariant();
        if (PortionWords.TryGetValue(u, out var words))
            foreach (var word in words)
                if (food.Portions.FirstOrDefault(p => p.Unit.Split([' ', ',', '(']).Contains(word)) is { } portion)
                    return (q * portion.Grams / portion.Amount, false);

        // Spoons and cups without a portion: through teaspoons/cups if USDA lists one, otherwise as water (rough)
        if (MlPer.TryGetValue(u, out var ml))
        {
            var cup = food.Portions.FirstOrDefault(p => p.Unit.StartsWith("cup"));
            if (cup is not null) return (q * ml / MlPer["cup"] * cup.Grams / cup.Amount, true);
            var tbsp = food.Portions.FirstOrDefault(p => p.Unit.StartsWith("tbsp") || p.Unit.StartsWith("tablespoon"));
            if (tbsp is not null) return (q * ml / MlPer["tbsp"] * tbsp.Grams / tbsp.Amount, true);
            return (q * ml, true);
        }
        return null;
    }

    public record Line(string Ingredient, string? MatchedFood, double? Grams, double Calories, bool Counted, bool Approximate);

    public record Totals(double Calories, double Protein, double Carbs, double Fat, double Fiber, double Sugar, double SodiumMg);

    public static (Totals Recipe, List<Line> Lines) Calculate(IEnumerable<(string Name, decimal Quantity, string Unit, FoodFacts? Food)> ingredients)
    {
        double cal = 0, pro = 0, carb = 0, fat = 0, fib = 0, sug = 0, na = 0;
        var lines = new List<Line>();
        foreach (var (name, quantity, unit, food) in ingredients)
        {
            var grams = food is null ? null : ToGrams(quantity, unit, food);
            if (food is null || grams is null)
            {
                lines.Add(new Line(name, food?.Description, null, 0, false, false));
                continue;
            }
            var f = grams.Value.Grams / 100;
            var n = food.Per100g;
            cal += n.Calories * f; pro += n.Protein * f; carb += n.Carbs * f; fat += n.Fat * f;
            fib += n.Fiber * f; sug += n.Sugar * f; na += n.SodiumMg * f;
            lines.Add(new Line(name, food.Description, Math.Round(grams.Value.Grams), Math.Round(n.Calories * f), true, grams.Value.Approximate));
        }
        return (new Totals(cal, pro, carb, fat, fib, sug, na), lines);
    }
}
