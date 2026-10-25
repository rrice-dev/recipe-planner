using RecipePlanner.Api.Ai;

namespace RecipePlanner.Tests;

public class FakeIngredientScanner(params string[] found) : IIngredientScanner
{
    public bool IsAvailable => true;
    public byte[]? LastPhoto { get; private set; }

    public Task<IReadOnlyList<string>> ScanAsync(byte[] jpeg, CancellationToken cancellationToken)
    {
        LastPhoto = jpeg;
        return Task.FromResult<IReadOnlyList<string>>(found);
    }
}
