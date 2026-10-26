using System.Text.RegularExpressions;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Library;

// Turns a week's meal plan into one prep session. Pure logic, no AI and no database, so it's
// predictable and easy to test.
public static partial class PrepPlanner
{
    // USDA: cooked leftovers keep 3 to 4 days in the fridge. Anything eaten later should be frozen or cooked fresh.
    public const int FridgeDays = 4;
    public const int LongCookMinutes = 45;

    // Dishes that reheat well vs. ones that are best cooked right before eating
    private static readonly string[] CookAheadTechniques = ["Braising", "Stewing", "Simmering", "Roasting", "Baking", "Pressure Cooking", "Smoking", "Boiling"];
    private static readonly string[] CookFreshTechniques = ["Stir-Frying", "Deep-Frying", "Pan-Frying", "Air Frying", "Searing", "Grilling", "Broiling", "Steaming", "Poaching"];

    // "finely chopped", "thinly sliced", "cut into 1-inch pieces"... the knife work an ingredient needs
    [GeneratedRegex(@"\b((?:very\s+|finely\s+|thinly\s+|roughly\s+|coarsely\s+|thickly\s+)?(?:chopped|diced|minced|sliced|grated|julienned|cubed|shredded|peeled|zested|juiced|crushed|smashed|trimmed|halved|quartered|riced|torn|cut into [^,;()]+|in wedges|in slices|into florets|florets))\b", RegexOptions.IgnoreCase)]
    private static partial Regex PrepWork();

    public enum Approach { CookAhead, PrepAheadCookFresh, Freeze }

    public record PlannedMeal(DateOnly Day, Recipe Recipe, int Servings);

    public record MealAdvice(DateOnly Day, int RecipeId, string Title, int Servings, Approach Approach, string Reason);

    public record PrepItem(string Ingredient, string Prep, decimal Quantity, string Unit, string? Section, List<string> ForRecipes);

    public record LongCook(int RecipeId, string Title, int Minutes, string FirstStep);

    public record PrepSession(List<MealAdvice> Meals, List<LongCook> StartFirst, List<PrepItem> ChopOnce, List<string> Marinate, DateOnly PrepDay);

    public static PrepSession Build(IReadOnlyList<PlannedMeal> meals, DateOnly prepDay)
    {
        var ordered = meals.OrderBy(m => m.Day).ToList();
        var advice = ordered.Select(m => Advise(m, prepDay)).ToList();

        // Long cooks for the dishes being cooked today, longest first, so they run while you chop
        var startFirst = ordered
            .Zip(advice)
            .Where(x => x.Second.Approach != Approach.PrepAheadCookFresh && x.First.Recipe.CookMinutes >= LongCookMinutes)
            .DistinctBy(x => x.First.Recipe.Id)
            .OrderByDescending(x => x.First.Recipe.CookMinutes)
            .Select(x => new LongCook(x.First.Recipe.Id, x.First.Recipe.Title, x.First.Recipe.CookMinutes,
                x.First.Recipe.Steps.OrderBy(s => s.Order).FirstOrDefault()?.Instruction ?? "Start cooking."))
            .ToList();

        // Each ingredient's knife work, combined across the week and scaled to the planned servings
        var chop = new Dictionary<(string, string, string), PrepItem>();
        foreach (var meal in ordered)
        {
            var factor = meal.Recipe.Servings > 0 ? (decimal)meal.Servings / meal.Recipe.Servings : 1;
            foreach (var ri in meal.Recipe.Ingredients)
            {
                if (ri.Note is null || PrepWork().Match(ri.Note) is not { Success: true } m) continue;
                var prep = Normalize(m.Value);
                var key = (ri.Ingredient.Name.ToLowerInvariant(), prep, ri.Unit.ToLowerInvariant());
                var qty = ri.Quantity * factor;
                chop[key] = chop.TryGetValue(key, out var existing)
                    ? existing with { Quantity = existing.Quantity + qty, ForRecipes = [.. existing.ForRecipes.Append(meal.Recipe.Title).Distinct()] }
                    : new PrepItem(ri.Ingredient.Name, prep, qty, ri.Unit, ri.Ingredient.StoreSection, [meal.Recipe.Title]);
            }
        }
        var chopOnce = chop.Values
            .Select(p => p with { Quantity = Math.Round(p.Quantity, 2) })
            .OrderBy(p => StoreSections.Rank(p.Section)).ThenBy(p => p.Ingredient).ThenBy(p => p.Prep)
            .ToList();

        var marinate = ordered
            .Where(m => m.Recipe.Steps.Any(s => s.Technique?.Name == "Marinating" || s.Instruction.Contains("marinat", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Recipe.Title).Distinct().ToList();

        return new PrepSession(advice, startFirst, chopOnce, marinate, prepDay);
    }

    private static MealAdvice Advise(PlannedMeal m, DateOnly prepDay)
    {
        var techniques = m.Recipe.Steps.Select(s => s.Technique?.Name).Concat(m.Recipe.Techniques.Select(t => t.Name))
            .Where(n => n is not null).Select(n => n!).ToHashSet();
        var reheatsWell = techniques.Any(CookAheadTechniques.Contains) && !techniques.Any(CookFreshTechniques.Contains)
            || techniques.Contains("Braising") || techniques.Contains("Stewing");
        var daysOut = m.Day.DayNumber - prepDay.DayNumber;

        if (!reheatsWell)
            return new(m.Day, m.Recipe.Id, m.Recipe.Title, m.Servings, Approach.PrepAheadCookFresh,
                "Best cooked fresh. Prep the ingredients today so it's quick on the night.");
        if (daysOut >= FridgeDays)
            return new(m.Day, m.Recipe.Id, m.Recipe.Title, m.Servings, Approach.Freeze,
                $"Eaten {daysOut} days after prep day, past the 3 to 4 days cooked food keeps in the fridge. Cook it today and freeze it, then thaw it in the fridge the day before.");
        return new(m.Day, m.Recipe.Id, m.Recipe.Title, m.Servings, Approach.CookAhead,
            "Reheats well. Cook it today, cool it, and refrigerate.");
    }

    private static string Normalize(string prep) =>
        prep.Trim().ToLowerInvariant().Replace("in wedges", "cut into wedges").Replace("in slices", "sliced");
}
