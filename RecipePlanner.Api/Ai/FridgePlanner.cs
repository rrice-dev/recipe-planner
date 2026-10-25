using System.Text.RegularExpressions;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Ai;

public static partial class FridgePlanner
{
    public const int MaxIngredients = 25;
    public const int MaxIngredientLength = 40;
    public const int MaxIdeas = 3;
    private const int MaxRecipeMatches = 5;
    private const int MaxIdeaNameLength = 80;
    private const int MaxDescriptionLength = 400;
    private const int MaxListItems = 15;
    private const int MaxListItemLength = 60;

    // Assumed to be in every kitchen, so never listed as "missing"
    private static readonly HashSet<string> PantryStaples = new(StringComparer.OrdinalIgnoreCase)
    {
        "salt", "pepper", "black pepper", "water", "oil", "olive oil", "vegetable oil", "cooking spray"
    };

    // Letters (any language), numbers, spaces and a little punctuation. No < > { } quotes or newlines,
    // which shrinks what a prompt-injection attempt can even express.
    [GeneratedRegex(@"^[\p{L}\p{M}\p{N} '\-.,&()/]+$")]
    private static partial Regex AllowedIngredient();

    // ---------- Input ----------

    public static (List<string> Ingredients, Dictionary<string, string[]> Errors) Validate(FridgeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var cleaned = (request.Ingredients ?? [])
            .Select(i => (i ?? "").Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
            errors["Ingredients"] = ["Add at least one ingredient."];
        else if (cleaned.Count > MaxIngredients)
            errors["Ingredients"] = [$"Use {MaxIngredients} ingredients or fewer."];
        else if (cleaned.Any(i => i.Length > MaxIngredientLength))
            errors["Ingredients"] = [$"Each ingredient must be {MaxIngredientLength} characters or fewer."];
        else if (cleaned.Any(i => !AllowedIngredient().IsMatch(i)))
            errors["Ingredients"] = ["Ingredients can only contain letters, numbers, spaces and basic punctuation."];

        return (cleaned, errors);
    }

    // Scanned names go through the same rules as typed ones; anything that wouldn't pass is dropped
    public static List<string> CleanScannedIngredients(IEnumerable<string> raw) =>
        raw.Select(i => (i ?? "").Trim())
            .Where(i => i.Length > 0 && i.Length <= MaxIngredientLength && AllowedIngredient().IsMatch(i))
            .Select(i => i.ToLowerInvariant())
            .Distinct()
            .Take(MaxIngredients)
            .ToList();

    // ---------- Your own recipes (no AI involved) ----------

    public static List<FridgeRecipeMatch> MatchRecipes(IEnumerable<Recipe> recipes, IReadOnlyList<string> onHand) =>
        recipes
            .Select(r =>
            {
                var needed = r.Ingredients.Select(ri => ri.Ingredient.Name)
                    .Where(n => !PantryStaples.Contains(n))
                    .ToList();
                var have = needed.Where(n => onHand.Any(h => SameIngredient(n, h))).ToList();
                var missing = needed.Except(have).ToList();
                return new FridgeRecipeMatch(r.Id, r.Title, have, missing);
            })
            .Where(m => m.Have.Count > 0)
            .OrderBy(m => m.Missing.Count)
            .ThenByDescending(m => m.Have.Count)
            .ThenBy(m => m.Title)
            .Take(MaxRecipeMatches)
            .ToList();

    // "chicken" matches "chicken thighs" and vice versa
    private static bool SameIngredient(string recipeIngredient, string onHand) =>
        recipeIngredient.Contains(onHand, StringComparison.OrdinalIgnoreCase) ||
        onHand.Contains(recipeIngredient, StringComparison.OrdinalIgnoreCase);

    // ---------- AI output: never trusted as-is ----------

    public static List<FridgeMealIdea> CleanIdeas(IEnumerable<RawMealIdea> raw, IReadOnlyList<Technique> techniques)
    {
        var byName = techniques.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        return raw
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Take(MaxIdeas)
            .Select(i => new FridgeMealIdea(
                Truncate(i.Name!, MaxIdeaNameLength),
                Truncate(i.Description ?? "", MaxDescriptionLength),
                CleanList(i.UsesIngredients),
                CleanList(i.ExtraIngredients),
                // Only techniques that really exist in the library, linked by ID.
                // Anything the model invented is dropped.
                (i.Techniques ?? [])
                    .Select(n => byName.GetValueOrDefault(n.Trim()))
                    .OfType<Technique>()
                    .DistinctBy(t => t.Id)
                    .Select(t => new TechniqueSummary(t.Id, t.Name, t.Difficulty))
                    .ToList()))
            .ToList();
    }

    private static List<string> CleanList(List<string>? items) =>
        (items ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Truncate(s, MaxListItemLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToList();

    private static string Truncate(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..max].TrimEnd() + "…";
    }
}
