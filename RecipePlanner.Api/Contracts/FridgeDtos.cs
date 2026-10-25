namespace RecipePlanner.Api.Contracts;

public record FridgeRequest(List<string> Ingredients);

public record FridgeResponse(
    List<FridgeRecipeMatch> FromYourRecipes,
    List<FridgeMealIdea> NewIdeas,
    bool AiAvailable,
    string? AiNotice);

public record FridgeRecipeMatch(int RecipeId, string Title, List<string> Have, List<string> Missing);

public record FridgeMealIdea(
    string Name,
    string Description,
    List<string> UsesIngredients,
    List<string> ExtraIngredients,
    List<TechniqueSummary> Techniques);

public record ScanResponse(List<string> Ingredients);

public record FridgeRecipeRequest(string DishName, List<string> Ingredients);

public record RecipeDraftResponse(
    string Title,
    string Description,
    int Servings,
    int PrepMinutes,
    int CookMinutes,
    List<RecipeDraftIngredient> Ingredients,
    List<RecipeDraftStep> Steps);

public record RecipeDraftIngredient(string Name, decimal Quantity, string Unit, string? Note, bool OnHand);

public record RecipeDraftStep(string Instruction, int? TechniqueId, string? TechniqueName);
