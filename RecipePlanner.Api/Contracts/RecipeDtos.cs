namespace RecipePlanner.Api.Contracts;

public record RecipeRequest(
    string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes,
    string? SourceUrl = null);   // for imported recipes: the page it came from

public record RecipeResponse(
    int Id, string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes,
    string? PhotoVersion = null,    // null = no photo; changes whenever the photo does, so browsers re-download it
    // For searching and filtering your book (filled in on the list endpoint)
    bool IsFavorite = false,
    int TimesCooked = 0,
    DateOnly? LastCooked = null,
    List<int>? CollectionIds = null,
    List<string>? IngredientNames = null,
    List<string>? DietTags = null);

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
    string? PhotoVersion,
    RecipePersonalResponse? Personal = null,
    string? SourceUrl = null);

// Only the owner sees this: favorite, private notes, when you've cooked it, and its collections
public record RecipePersonalResponse(bool IsFavorite, string? Notes, List<CookLogResponse> Cooked, List<int> CollectionIds);

public record CookLogResponse(int Id, DateOnly CookedOn);

public record FavoriteRequest(bool IsFavorite);

public record NotesRequest(string? Notes);

public record CookedRequest(DateOnly? CookedOn);

public record CollectionRequest(string Name);

public record CollectionResponse(int Id, string Name, int RecipeCount);

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
public record ImportRequest(string Url);

/// <param name="Method">"structured" when read from the page's recipe data, "ai" when the AI read the page text</param>
public record ImportResponse(RecipeDraftResponse Recipe, string SourceUrl, string Method);
