namespace RecipePlanner.Api.Contracts;

/// <param name="Status">"ok"; "unavailable" when Kroger isn't set up on the server; "no-store" until you connect Kroger and pick a store</param>
public record RecipeCostResponse(string Status, string? StoreName, int Servings, decimal? Total, decimal? PerServing,
    int Counted, int TotalIngredients, List<CostLine> Lines);

/// <param name="Cost">What the amount used costs; null when it couldn't be priced</param>
/// <param name="Approximate">A weight was worked out from a volume, or the other way round</param>
public record CostLine(string Ingredient, string? Product, string? Size, decimal? PackagePrice, decimal? Cost, bool Approximate);

public record PlanMealCost(DateOnly Day, int RecipeId, string Title, int Servings, decimal? Cost, decimal? PerServing, bool Complete);

/// <param name="Total">All meals at their planned servings</param>
public record PlanCostResponse(string Status, string? StoreName, List<PlanMealCost> Meals, decimal? Total, decimal? Budget, bool Complete);

public record BudgetSettings(decimal? WeeklyBudget);
