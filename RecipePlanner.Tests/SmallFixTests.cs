using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Ai;

namespace RecipePlanner.Tests;

// Small items from the second code review (Oct 29). Each test failed before its fix.
public class SmallFixTests
{
    [Fact]
    public async Task A_garbled_reply_from_kroger_says_kroger_is_down_instead_of_crashing()
    {
        var kroger = new FakeKroger { GarbledLocations = true };
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await factory.CreateUserClientAsync();

        var response = await client.GetAsync("/api/kroger/locations?zip=45249");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public void An_ai_unit_that_already_fits_is_left_alone()
    {
        var twenty = "heaping tablespoons."; // exactly 20 characters
        Assert.Equal(20, twenty.Length);
        var raw = new RawRecipe("Soup", null, 2, 5, 20, [new("Salt", 1, twenty, null), new("Pepper", 1, "teaspoons, freshly cracked", null)], [new("Stir", null)]);

        var draft = FridgePlanner.CleanRecipe(raw, [], [])!;

        Assert.Equal(twenty, draft.Ingredients.Single(i => i.Name == "Salt").Unit);
        var pepper = draft.Ingredients.Single(i => i.Name == "Pepper").Unit;
        Assert.True(pepper.Length <= 20 && pepper.EndsWith('…'), pepper);
    }
}
