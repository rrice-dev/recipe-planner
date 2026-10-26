namespace RecipePlanner.Api.Contracts;

public record GroceryItemResponse(
    int Id, string Name, decimal? Quantity, string? Unit, string? StoreSection, bool Checked, string? Sources);

public record GroceryAddRequest(string Name, decimal? Quantity, string? Unit, string? Source);

public record GroceryFromRecipeRequest(int? Servings);

public record GroceryUpdateRequest(bool? Checked, decimal? Quantity);

// Adding a recipe or plan: the updated list, plus which pantry staples were left off
public record GroceryAddResult(List<GroceryItemResponse> Items, List<string> SkippedPantry);
