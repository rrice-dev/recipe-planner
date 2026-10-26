namespace RecipePlanner.Api.Models;

public class Recipe
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Cuisine { get; set; }
    public int Servings { get; set; }
    public int PrepMinutes { get; set; }
    public int CookMinutes { get; set; }
    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public List<Step> Steps { get; set; } = [];
    public List<Technique> Techniques { get; set; } = [];
    public string OwnerId { get; set; } = null!;

    // Set when the recipe was copied from the built-in library, so planning a week can reuse the copy
    public string? LibrarySlug { get; set; }

    // Set when the owner shares the recipe. A long random token, never the ID, so links can't be guessed.
    public string? ShareToken { get; set; }
    public DateTimeOffset? SharedAt { get; set; }
    public List<RecipeRating> Ratings { get; set; } = [];

    // Name of the photo in storage: random, so it reveals nothing and can't be guessed
    public string? PhotoName { get; set; }
    public DateTimeOffset? PhotoUpdatedAt { get; set; }
}

// One rating per user per recipe. The owner's own rating is their private note;
// everyone else's ratings (made through a share link) make up the community average.
public class RecipeRating
{
    public int RecipeId { get; set; }
    public string UserId { get; set; } = null!;
    public int Stars { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Ingredient
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? StoreSection { get; set; }
}

public class RecipeIngredient
{
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int IngredientId { get; set; }
    public Ingredient Ingredient { get; set; } = null!;
    public decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public string? Note { get; set; }
}

public class Step
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int Order { get; set; }
    public required string Instruction { get; set; }
    public int? TechniqueId { get; set; }
    public Technique? Technique { get; set; }
}

public class Technique
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Summary { get; set; }
    public string? DonenessCues { get; set; }
    public string? CommonMistakes { get; set; }
    public int Difficulty { get; set; }
    public List<Recipe> Recipes { get; set; } = [];
}

public class MealPlan
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateOnly WeekStart { get; set; }
    public List<MealPlanEntry> Entries { get; set; } = [];
    public string OwnerId { get; set; } = null!;
}

public class MealPlanEntry
{
    public int Id { get; set; }
    public int MealPlanId { get; set; }
    public DateOnly Day { get; set; }
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int Servings { get; set; }
}
// One line on a user's shopping list. Linked to an ingredient when it matches one,
// or free text (like "paper towels") when it doesn't.
public class GroceryItem
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = null!;
    public int? IngredientId { get; set; }
    public Ingredient? Ingredient { get; set; }
    public required string Name { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? StoreSection { get; set; }
    public bool Checked { get; set; }
    public string? Sources { get; set; }      // e.g. "Lemon Herb Chicken, Week of Oct 26"
    public DateTimeOffset AddedAt { get; set; }
}

// A user's link to their Kroger account. Tokens are encrypted before they're saved.
public class KrogerConnection
{
    public string UserId { get; set; } = null!;
    public required string EncryptedRefreshToken { get; set; }
    public string? EncryptedAccessToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
}

// Remembers which Kroger product a user picked for an item ("Butter" -> their usual brand),
// so next time it's preselected
public class KrogerProductChoice
{
    public string UserId { get; set; } = null!;
    public string ItemKey { get; set; } = null!;    // lowercased item name
    public required string Upc { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
