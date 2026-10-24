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
