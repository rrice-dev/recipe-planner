using RecipePlanner.Api.Ai;

namespace RecipePlanner.Tests;

public class FakeCountryDishes(params RawCountryDish[] dishes) : ICountryDishGenerator
{
    public bool IsAvailable => true;
    public int Calls { get; private set; }
    public string? LastCountry { get; private set; }
    public IReadOnlyList<string>? LastAlreadyInLibrary { get; private set; }
    public Exception? Throw { get; init; }

    public Task<IReadOnlyList<RawCountryDish>> GenerateAsync(string country, IReadOnlyList<string> alreadyInLibrary,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastCountry = country;
        LastAlreadyInLibrary = alreadyInLibrary;
        if (Throw is not null) throw Throw;
        return Task.FromResult<IReadOnlyList<RawCountryDish>>(dishes);
    }
}
