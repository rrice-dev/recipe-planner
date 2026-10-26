using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Library;

// Picks dinners for a week from the library and the user's own recipes.
// Pure logic, no database, so it's easy to test.
public static class WeekPlanner
{
    public const int MaxDays = 14;

    // Not dinners on their own, so they're left out unless asked for by tag
    private static readonly string[] NotADinner = ["Side", "Dessert", "Breakfast"];

    private static readonly HashSet<string> AsianCuisines = new(StringComparer.OrdinalIgnoreCase)
        { "Chinese", "Japanese", "Korean", "Thai", "Vietnamese", "Indian", "Filipino", "Indonesian", "Malaysian" };

    public static string CuisineFamily(string? cuisine) =>
        System.Text.RegularExpressions.Regex.Replace(cuisine ?? "", @"\s*\(.*\)$", "").Replace("-inspired", "").Trim();

    /// <summary>A recipe the planner can choose from, from either source</summary>
    public record Option(PlanCandidate Candidate, Func<string, bool> FitsDiet);

    public static Option FromLibrary(LibraryRecipe r) => new(
        new PlanCandidate($"lib:{r.Slug}", r.Title, r.Cuisine, r.PrepMinutes + r.CookMinutes, RecipeLibrary.TagsFor(r).ToList(),
            true, null, r.Slug, null),
        diet => r.Tags.Contains(diet));

    public static Option FromMine(Recipe r, string? photoVersion)
    {
        var ingredients = r.Ingredients.Select(i => (i.Ingredient.Name, i.Ingredient.StoreSection)).ToList();
        var total = r.PrepMinutes + r.CookMinutes;
        var tags = new List<string>();
        if (ingredients.Count > 0 && Diet.Fits(Diet.Aip, ingredients)) tags.Add(Diet.Aip);
        if (ingredients.Count > 0 && Diet.Fits(Diet.Vegetarian, ingredients)) tags.Add(Diet.Vegetarian);
        if (AsianCuisines.Contains(CuisineFamily(r.Cuisine))) tags.Add("Asian");
        if (total is > 0 and <= RecipeLibrary.QuickMaxMinutes) tags.Add(RecipeLibrary.QuickTag);
        return new(new PlanCandidate($"mine:{r.Id}", r.Title, r.Cuisine, total, tags, false, r.Id, r.LibrarySlug, photoVersion),
            // A recipe with no ingredients can't be checked, so it never counts as fitting a diet
            diet => tags.Contains(diet));
    }

    public static (List<PlanCandidate> Picks, int Matching) Suggest(
        IEnumerable<Option> options, int count, string? diet, string? tag, string? cuisine, ICollection<string> exclude, Random random)
    {
        var pool = options
            .Where(o => diet is null || o.FitsDiet(diet))
            .Where(o => tag is null || o.Candidate.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Where(o => tag is not null && NotADinner.Contains(tag) || !o.Candidate.Tags.Any(t => NotADinner.Contains(t)))
            .Where(o => cuisine is null || string.Equals(CuisineFamily(o.Candidate.Cuisine), cuisine, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Candidate)
            .ToList();

        // Your copy of a library recipe stands in for the library original, never both
        var copied = pool.Where(c => !c.FromLibrary && c.Slug is not null).Select(c => c.Slug!).ToHashSet();
        pool = pool.Where(c => !(c.FromLibrary && copied.Contains(c.Slug!))).ToList();

        var matching = pool.Count;
        var available = pool.Where(c => !exclude.Contains(c.Key)).OrderBy(_ => random.Next()).ToList();

        // Variety: avoid the same cuisine two days running when there's another choice
        var picks = new List<PlanCandidate>();
        while (picks.Count < count && available.Count > 0)
        {
            var last = picks.Count > 0 ? CuisineFamily(picks[^1].Cuisine) : null;
            var next = available.FirstOrDefault(c => CuisineFamily(c.Cuisine) != last) ?? available[0];
            picks.Add(next);
            available.Remove(next);
        }
        return (picks, matching);
    }
}
