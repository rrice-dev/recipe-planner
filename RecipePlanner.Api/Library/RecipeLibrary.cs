using System.Text.Json;

namespace RecipePlanner.Api.Library;

// The built-in recipe collection. It ships as a JSON file inside the app, not as database rows,
// so it needs no migration and every deploy carries the latest version. Users browse it read-only;
// "Add to my recipes" copies one into their own book, where they can change it freely.
public sealed class RecipeLibrary
{
    public const string QuickTag = "Quick";
    public const int QuickMaxMinutes = 30;

    private readonly Dictionary<string, LibraryRecipe> _bySlug;

    public IReadOnlyList<LibraryRecipe> All { get; }

    // Store section for each seed ingredient, used when a copied ingredient isn't in the database yet
    public IReadOnlyDictionary<string, string?> SeedSections { get; }

    public RecipeLibrary(IReadOnlyList<LibraryRecipe> recipes, IReadOnlyDictionary<string, string?> seedSections)
    {
        All = recipes.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
        _bySlug = All.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);
        SeedSections = seedSections;
    }

    public LibraryRecipe? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    public static IReadOnlyList<string> TagsFor(LibraryRecipe r) =>
        r.PrepMinutes + r.CookMinutes <= QuickMaxMinutes ? [.. r.Tags, QuickTag] : r.Tags;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static RecipeLibrary Load(string contentRoot)
    {
        var dataDir = Path.Combine(contentRoot, "Data");
        var recipes = JsonSerializer.Deserialize<List<LibraryRecipe>>(
            File.ReadAllText(Path.Combine(dataDir, "Library", "recipes.json")), Options)!;
        var seed = JsonSerializer.Deserialize<List<SeedIngredient>>(
            File.ReadAllText(Path.Combine(dataDir, "Seed", "ingredients.json")), Options)!;

        var sections = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in seed) sections.TryAdd(i.Name.Trim(), i.StoreSection);

        return new RecipeLibrary(recipes, sections);
    }

    private record SeedIngredient(string Name, string? StoreSection);
}

public record LibraryRecipe(
    string Slug,
    string Title,
    string Description,
    string Cuisine,
    int Servings,
    int PrepMinutes,
    int CookMinutes,
    List<string> Tags,
    List<LibraryIngredient> Ingredients,
    List<LibraryStep> Steps);

public record LibraryIngredient(string Name, decimal Quantity, string Unit, string? Note);

public record LibraryStep(string Instruction, string? Technique);

public static class RecipeLibraryExtensions
{
    public static IServiceCollection AddRecipeLibrary(this IServiceCollection services, string contentRoot) =>
        // Loaded once at startup: a bad file fails fast instead of on the first visitor
        services.AddSingleton(RecipeLibrary.Load(contentRoot));
}
