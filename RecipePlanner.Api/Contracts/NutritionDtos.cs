namespace RecipePlanner.Api.Contracts;

public record FeaturesResponse(bool PasswordReset, bool Nutrition);

public record NutrientsResponse(int Calories, double Protein, double Carbs, double Fat, double Fiber, double Sugar, int SodiumMg);

public record NutritionLineResponse(string Ingredient, string? MatchedFood, double? Grams, double Calories, bool Counted, bool Approximate);

/// <param name="PerServing">Null when nutrition isn't set up</param>
/// <param name="Counted">How many ingredients could be counted; the rest had no match or no weight</param>
public record NutritionResponse(bool Available, int Servings, NutrientsResponse? PerServing, int Counted, int Total, List<NutritionLineResponse> Lines);

/// <summary>Daily targets; any can be null for "no target"</summary>
public record NutritionTargets(int? Calories, int? Protein, int? Carbs, int? Fat);

/// <param name="PerServing">One serving of this meal; null when none of its ingredients could be counted</param>
/// <param name="Complete">False when some ingredients couldn't be counted, so the numbers are low</param>
public record PlanMealNutrition(int RecipeId, string Title, NutrientsResponse? PerServing, bool Complete);

/// <param name="Total">One serving of each meal planned that day</param>
public record PlanDayNutrition(DateOnly Day, List<PlanMealNutrition> Meals, NutrientsResponse Total);

/// <param name="DailyAverage">Average over the days that have meals</param>
public record PlanNutritionResponse(bool Available, List<PlanDayNutrition> Days, NutrientsResponse? DailyAverage, NutritionTargets Targets, bool Complete);

/// <param name="Calories">One serving; null when none of its ingredients could be counted</param>
/// <param name="Complete">False when some ingredients couldn't be counted, so the number is low</param>
public record RecipeCalories(int RecipeId, int? Calories, bool Complete);

/// <param name="Pending">Some ingredients haven't been looked up yet; ask again to finish them</param>
public record RecipeCaloriesResponse(bool Available, bool Pending, List<RecipeCalories> Recipes);
