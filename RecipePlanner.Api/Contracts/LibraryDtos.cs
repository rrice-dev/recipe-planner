namespace RecipePlanner.Api.Contracts;

public record LibrarySummaryResponse(
    string Slug, string Title, string Description, string Cuisine,
    int Servings, int PrepMinutes, int CookMinutes, IReadOnlyList<string> Tags, int IngredientCount);

public record LibraryIngredientResponse(string Name, decimal Quantity, string Unit, string? Note, string? StoreSection);

public record LibraryStepResponse(int Order, string Instruction, string? Technique);

public record LibraryDetailResponse(
    string Slug, string Title, string Description, string Cuisine,
    int Servings, int PrepMinutes, int CookMinutes, IReadOnlyList<string> Tags,
    IReadOnlyList<LibraryIngredientResponse> Ingredients, IReadOnlyList<LibraryStepResponse> Steps);

public record LibraryAddResponse(int RecipeId);
