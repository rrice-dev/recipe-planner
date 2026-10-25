namespace RecipePlanner.Api.Contracts;

public record KrogerStatusResponse(bool Available, bool Connected, string? LocationId, string? LocationName);

public record KrogerStoreRequest(string LocationId, string Name);

public record KrogerProductOption(string Upc, string Description, string? Brand, string? Size, decimal? Price, decimal? PromoPrice, string? ImageUrl);

public record KrogerMatch(int GroceryItemId, string Name, string? Amount, List<KrogerProductOption> Options, string? SelectedUpc);

public record KrogerCartItem(int GroceryItemId, string Upc, string Description, int Quantity);

public record KrogerCartRequest(string Modality, List<KrogerCartItem> Items, bool MarkChecked = true);

public record KrogerCartResult(int Added);
