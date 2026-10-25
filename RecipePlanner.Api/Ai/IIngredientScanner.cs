namespace RecipePlanner.Api.Ai;

public interface IIngredientScanner
{
    bool IsAvailable { get; }

    // Returns the raw names the model saw. Untrusted: the endpoint cleans them before use.
    Task<IReadOnlyList<string>> ScanAsync(byte[] jpeg, CancellationToken cancellationToken);
}

public sealed class DisabledIngredientScanner : IIngredientScanner
{
    public bool IsAvailable => false;
    public Task<IReadOnlyList<string>> ScanAsync(byte[] jpeg, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
