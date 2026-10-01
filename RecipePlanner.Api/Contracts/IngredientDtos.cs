namespace RecipePlanner.Api.Contracts;

public record IngredientRequest(string Name, string? StoreSection);

public record IngredientResponse(int Id, string Name, string? StoreSection);