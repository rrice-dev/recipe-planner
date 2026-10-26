namespace RecipePlanner.Api.Kitchen;

// Something you always keep on hand (salt, olive oil, rice). Adding a recipe or meal plan to the
// shopping list skips these, so the list only has what you actually need to buy.
public class PantryItem
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = null!;
    public required string Name { get; set; }
    public required string NameKey { get; set; }   // lowercased, single-spaced, for matching
}

// Cooked food waiting to be eaten: how many portions are left, where they are, and when to eat them by
public class Leftover
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = null!;
    public required string Name { get; set; }
    public int? RecipeId { get; set; }
    public int Portions { get; set; }
    public required string Location { get; set; }  // "fridge" or "freezer"
    public DateOnly MadeOn { get; set; }
    public DateOnly UseBy { get; set; }
}

public static class KitchenRules
{
    public const string Fridge = "fridge";
    public const string Freezer = "freezer";
    public const int MaxNameLength = 100;
    public const int MaxPantryItems = 200;
    public const int MaxLeftovers = 100;
    public const int MaxPortions = 50;

    // USDA: cooked leftovers keep 3 to 4 days in the fridge, and food thawed in the fridge should be
    // eaten within 3 to 4 days too. Frozen food stays safe, but is best within 2 to 3 months.
    public const int FridgeDays = 4;
    public const int FreezerDays = 90;

    public static string Key(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static bool IsLocation(string? location) => location is Fridge or Freezer;

    public static DateOnly UseBy(string location, DateOnly from) =>
        from.AddDays(location == Freezer ? FreezerDays : FridgeDays);
}
