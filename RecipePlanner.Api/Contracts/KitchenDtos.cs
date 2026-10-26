namespace RecipePlanner.Api.Contracts;

public record PantryItemResponse(int Id, string Name);

public record PantryAddRequest(List<string> Names);

public record LeftoverResponse(int Id, string Name, int? RecipeId, int Portions, string Location, DateOnly MadeOn, DateOnly UseBy);

public record LeftoverRequest(string? Name, int? RecipeId, int Portions, string Location, DateOnly? MadeOn);

// On: the user's local date, so "today" matches their kitchen, not the server's time zone
public record LeftoverUpdateRequest(int? Portions, string? Location, DateOnly? On);

public record LeftoverEatRequest(int? Portions);
