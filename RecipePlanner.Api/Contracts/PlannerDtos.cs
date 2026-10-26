namespace RecipePlanner.Api.Contracts;

/// <param name="Count">How many dinners to suggest (1-14)</param>
/// <param name="Diet">"AIP", "Vegetarian" or null; defaults to the user's setting when omitted</param>
/// <param name="Tag">Optional theme: "Asian", "Quick", "Soup"...</param>
/// <param name="Cuisine">Optional cuisine family, e.g. "Thai"</param>
/// <param name="Source">"both", "library" or "mine"</param>
/// <param name="Exclude">Keys already in the plan, so a swap doesn't suggest them again</param>
public record PlanSuggestRequest(int Count, string? Diet, string? Tag, string? Cuisine, string? Source, List<string>? Exclude, bool UseMyDiet = true);

/// <param name="Key">"lib:{slug}" for a library recipe, "mine:{id}" for one of yours</param>
public record PlanCandidate(string Key, string Title, string? Cuisine, int TotalMinutes, List<string> Tags,
    bool FromLibrary, int? RecipeId, string? Slug, string? PhotoVersion);

public record PlanSuggestResponse(List<PlanCandidate> Suggestions, int Matching, string? Diet);

public record PlanPick(DateOnly Day, string Key, int Servings);

public record CreatePlanRequest(string Name, DateOnly WeekStart, List<PlanPick> Picks);
