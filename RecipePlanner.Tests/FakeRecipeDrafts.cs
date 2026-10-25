using RecipePlanner.Api.Ai;

namespace RecipePlanner.Tests;

public class FakeRecipeDrafts(RawRecipe? recipe) : IRecipeDraftGenerator
{
    public bool IsAvailable => true;
    public string? LastDish { get; private set; }
    public IReadOnlyList<string>? LastOnHand { get; private set; }

    public Task<RawRecipe?> GenerateAsync(string dishName, IReadOnlyList<string> onHand,
        IReadOnlyList<string> techniqueNames, CancellationToken cancellationToken)
    {
        LastDish = dishName;
        LastOnHand = onHand;
        return Task.FromResult(recipe);
    }
}
