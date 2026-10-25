namespace RecipePlanner.Api.Ai;

public interface IRecipeDraftGenerator
{
    bool IsAvailable { get; }

    Task<RawRecipe?> GenerateAsync(
        string dishName,
        IReadOnlyList<string> onHand,
        IReadOnlyList<string> techniqueNames,
        CancellationToken cancellationToken);
}

// Exactly what the model returned. Untrusted until FridgePlanner.CleanRecipe has checked it.
public record RawRecipe(
    string? Title,
    string? Description,
    decimal? Servings,        // decimal, not int: a model answering "4.0" shouldn't break the whole request
    decimal? PrepMinutes,
    decimal? CookMinutes,
    List<RawRecipeIngredient>? Ingredients,
    List<RawRecipeStep>? Steps);

public record RawRecipeIngredient(string? Name, decimal? Quantity, string? Unit, string? Note);

public record RawRecipeStep(string? Instruction, string? Technique);

public sealed class DisabledRecipeDraftGenerator : IRecipeDraftGenerator
{
    public bool IsAvailable => false;
    public Task<RawRecipe?> GenerateAsync(string dishName, IReadOnlyList<string> onHand,
        IReadOnlyList<string> techniqueNames, CancellationToken cancellationToken) => Task.FromResult<RawRecipe?>(null);
}
