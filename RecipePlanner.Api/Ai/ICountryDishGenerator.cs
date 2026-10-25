namespace RecipePlanner.Api.Ai;

// Suggests well-known dishes from one country, for the world map
public interface ICountryDishGenerator
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<RawCountryDish>> GenerateAsync(
        string country, IReadOnlyList<string> alreadyInLibrary, CancellationToken cancellationToken);
}

// Exactly what the model returned. Untrusted until WorldEndpoints cleans it.
public record RawCountryDish(string? Name, string? Description, bool? AipFriendly);

public sealed class DisabledCountryDishGenerator : ICountryDishGenerator
{
    public bool IsAvailable => false;
    public Task<IReadOnlyList<RawCountryDish>> GenerateAsync(string country, IReadOnlyList<string> alreadyInLibrary,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RawCountryDish>>([]);
}
