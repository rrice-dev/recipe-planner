namespace RecipePlanner.Api.Contracts;

public record RecipeRequest(
    string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes);

public record RecipeResponse(
    int Id, string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes);

public record RecipeIngredientRequest(int IngredientId, decimal Quantity, string Unit, string? Note);

public record StepRequest(string Instruction, int? TechniqueId);

public record RecipeIngredientResponse(
    int IngredientId, string Name, decimal Quantity, string Unit,
    string? Note, string? StoreSection);

public record StepResponse(int Order, string Instruction, int? TechniqueId, string? TechniqueName);

public record RecipeDetailResponse(
    int Id, string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes,
    List<TechniqueSummary> Techniques,
    List<RecipeIngredientResponse> Ingredients,
    List<StepResponse> Steps);