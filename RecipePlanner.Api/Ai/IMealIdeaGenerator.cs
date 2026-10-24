namespace RecipePlanner.Api.Ai;

// The rest of the app only talks to this interface, so the AI provider can be
// swapped (or faked in tests) without touching the endpoint.
public interface IMealIdeaGenerator
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<RawMealIdea>> GenerateAsync(
        IReadOnlyList<string> ingredients,
        IReadOnlyList<string> techniqueNames,
        CancellationToken cancellationToken);
}

// Exactly what the model returned. Treated as untrusted until FridgePlanner cleans it up.
public record RawMealIdea(
    string? Name,
    string? Description,
    List<string>? UsesIngredients,
    List<string>? ExtraIngredients,
    List<string>? Techniques);

// Used when no API key is configured (local dev, tests, or if you turn the feature off)
public sealed class DisabledMealIdeaGenerator : IMealIdeaGenerator
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<RawMealIdea>> GenerateAsync(
        IReadOnlyList<string> ingredients, IReadOnlyList<string> techniqueNames, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RawMealIdea>>([]);
}
