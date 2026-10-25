namespace RecipePlanner.Api.Contracts;

public record RecipeRequest(
    string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes);

public record RecipeResponse(
    int Id, string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes,
    string? PhotoVersion = null);   // null = no photo; changes whenever the photo does, so browsers re-download it

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
    List<StepResponse> Steps,
    RecipeSocialResponse Social,
    string? PhotoVersion);

// Owner's view of sharing and ratings for one of their recipes
public record RecipeSocialResponse(string? ShareToken, int? MyRating, double? CommunityAverage, int CommunityCount);

public record RatingRequest(int Stars);

public record RatingSummary(double? Average, int Count, int? MyRating, bool IsOwner);

// What anyone with the link sees. No IDs, no owner details.
public record SharedRecipeResponse(
    string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes,
    List<string> Techniques,
    List<SharedIngredient> Ingredients,
    List<SharedStep> Steps,
    RatingSummary Rating,
    string? PhotoVersion);

public record SharedIngredient(string Name, decimal Quantity, string Unit, string? Note);

public record SharedStep(int Order, string Instruction, string? TechniqueName);