namespace RecipePlanner.Api.Contracts;

public record CountryDishesRequest(string Country);

public record CountryDish(string Name, string Description, bool AipFriendly);

public record CountryDishesResponse(string Country, List<CountryDish> Dishes, bool AiAvailable, string? AiNotice);

public record CountryRecipeRequest(string Country, string DishName);
