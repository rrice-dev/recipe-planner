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