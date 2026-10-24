using RecipePlanner.Api.Ai;

namespace RecipePlanner.Tests;

public class FakeMealIdeas(params RawMealIdea[] ideas) : IMealIdeaGenerator
{
    public bool IsAvailable => true;
    public IReadOnlyList<string>? LastIngredients { get; private set; }
    public IReadOnlyList<string>? LastTechniques { get; private set; }
    public Exception? ThrowThis { get; init; }

    public Task<IReadOnlyList<RawMealIdea>> GenerateAsync(
        IReadOnlyList<string> ingredients, IReadOnlyList<string> techniqueNames, CancellationToken cancellationToken)
    {
        LastIngredients = ingredients;
        LastTechniques = techniqueNames;
        if (ThrowThis is not null) throw ThrowThis;
        return Task.FromResult<IReadOnlyList<RawMealIdea>>(ideas);
    }
}
