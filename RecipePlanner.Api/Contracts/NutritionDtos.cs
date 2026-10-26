namespace RecipePlanner.Api.Contracts;

public record FeaturesResponse(bool PasswordReset, bool Nutrition);

public record NutrientsResponse(int Calories, double Protein, double Carbs, double Fat, double Fiber, double Sugar, int SodiumMg);

public record NutritionLineResponse(string Ingredient, string? MatchedFood, double? Grams, double Calories, bool Counted, bool Approximate);

/// <param name="PerServing">Null when nutrition isn't set up</param>
/// <param name="Counted">How many ingredients could be counted; the rest had no match or no weight</param>
public record NutritionResponse(bool Available, int Servings, NutrientsResponse? PerServing, int Counted, int Total, List<NutritionLineResponse> Lines);
