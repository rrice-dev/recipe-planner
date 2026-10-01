namespace RecipePlanner.Api.Contracts;

public record RecipeRequest(
    string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes);

public record RecipeResponse(
    int Id, string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes);

    public record RecipeDetailResponse(
    int Id, string Title, string? Description, string? Cuisine,
    int Servings, int PrepMinutes, int CookMinutes,
    List<TechniqueSummary> Techniques);