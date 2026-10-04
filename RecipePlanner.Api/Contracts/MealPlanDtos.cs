namespace RecipePlanner.Api.Contracts;

public record MealPlanRequest(string Name, DateOnly WeekStart);

public record MealPlanEntryRequest(DateOnly Day, int RecipeId, int Servings);

public record MealPlanEntryResponse(DateOnly Day, int RecipeId, string RecipeTitle, int Servings);

public record MealPlanResponse(int Id, string Name, DateOnly WeekStart, List<MealPlanEntryResponse> Entries);

public record ShoppingListItem(string Ingredient, decimal Quantity, string Unit, string? StoreSection);

public record ShoppingListResponse(int MealPlanId, string Name, List<ShoppingListItem> Items);