using System.Text.RegularExpressions;

namespace RecipePlanner.Api.Library;

// Rules for the two diets the app understands. Used to check library recipes in tests, and the
// user's own recipes when planning a week, so both are judged the same way.
// These are ingredient-name checks, a helper and not a guarantee: users still read labels.
public static partial class Diet
{
    public const string Aip = "AIP";
    public const string Vegetarian = "Vegetarian";

    // The AIP elimination phase removes grains, legumes, dairy, eggs, nuts, seeds (including seed-based
    // spices), nightshades, refined sugar, seed oils, coffee and alcohol
    private static readonly string[] AipForbiddenSections = ["Grains & Pasta", "Bakery", "Dairy", "Nuts & Seeds", "Beverages"];

    public static readonly string[] AipForbiddenWords =
    [
        // Grains, legumes and soy
        "rice", "flour", "noodle", "pasta", "bread", "panko", "corn", "oat", "wheat", "barley", "quinoa", "bulgur", "masarepa",
        "bean", "lentil", "chickpea", "pea", "edamame", "tofu", "soy", "miso", "tamari", "peanut", "hoisin", "oyster sauce",
        // Dairy and eggs
        "egg", "butter", "milk", "cream", "cheese", "yogurt", "ghee", "curds",
        // Nightshades
        "tomato", "potato", "pepper", "chili", "chile", "eggplant", "paprika", "cayenne", "gochu", "sriracha",
        "curry", "doubanjiang", "kimchi", "salsa", "ketchup", "jalapeño", "habanero", "poblano", "serrano", "harissa", "berbere", "ají",
        // Seeds and seed spices
        "sesame", "cumin", "coriander", "fennel", "mustard", "nutmeg", "cardamom", "allspice", "star anise",
        "caraway", "chia", "flax", "poppy", "garam masala", "chili powder",
        // Sugar, seed oils, alcohol, coffee
        "sugar", "vegetable oil", "canola", "corn syrup", "wine", "mirin", "shaoxing", "beer", "coffee",
        "worcestershire", "rice vinegar", "kecap", "jam"
    ];

    // Allowed on AIP even though a forbidden word appears in the name
    private static readonly HashSet<string> AipAllowedExceptions = new(StringComparer.OrdinalIgnoreCase)
        { "Coconut Flour", "Cassava Flour", "Arrowroot Powder", "Tapioca Starch", "Coconut Milk", "Sweet Potato", "Cauliflower" };

    private static readonly string[] AnimalWords =
        ["chicken", "beef", "pork", "bacon", "fish", "oyster", "anchovy", "anchovies", "shrimp", "bone broth", "gelatin", "lard", "lamb", "turkey",
         "sausage", "chorizo", "kielbasa", "ham", "prosciutto", "pancetta", "pepperoni", "salmon", "tuna", "cod", "crab"];

    public static bool IsKnown(string? diet) => diet is Aip or Vegetarian;

    public static bool Fits(string diet, IEnumerable<(string Name, string? Section)> ingredients) => diet switch
    {
        Aip => ingredients.All(i => FitsAip(i.Name, i.Section)),
        Vegetarian => ingredients.All(i => FitsVegetarian(i.Name, i.Section)),
        _ => true
    };

    public static bool FitsAip(string name, string? section)
    {
        if (section is not null && AipForbiddenSections.Contains(section)) return false;
        if (AipAllowedExceptions.Contains(name)) return true;
        return !AipForbiddenWords.Any(w => Mentions(name, w));
    }

    public static bool FitsVegetarian(string name, string? section) =>
        section is not ("Meat" or "Seafood")
        // Whole words, so "Champagne vinegar" isn't mistaken for ham
        && !AnimalWords.Any(w => Mentions(name, w));

    // Whole-word match, with "peas"/"beans"/"noodles" style plurals
    public static bool Mentions(string ingredient, string word) =>
        Regex.IsMatch(ingredient, $@"\b{Regex.Escape(word)}(e?s)?\b", RegexOptions.IgnoreCase);
}
