using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class TechniqueEndpointTests
{
    private static TechniqueRequest Sample(string name = "Braising", int difficulty = 2) =>
        new(name, "Brown, then cook slowly in liquid.", "Fork-tender", "Boiling instead of simmering", difficulty);

    [Fact]
    public async Task Create_then_get_returns_technique()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/techniques", Sample());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<TechniqueResponse>();

        var fetched = await client.GetFromJsonAsync<TechniqueResponse>($"/api/techniques/{created!.Id}");
        Assert.Equal("Braising", fetched!.Name);
    }

    [Fact]
    public async Task Duplicate_name_returns_409()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/techniques", Sample());
        var duplicate = await client.PostAsJsonAsync("/api/techniques", Sample());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Invalid_difficulty_returns_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/techniques", Sample(difficulty: 9));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Linked_technique_appears_on_recipe_and_can_be_removed()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var recipe = await (await client.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Short Ribs", null, "Korean", 4, 20, 180)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var technique = await (await client.PostAsJsonAsync("/api/techniques", Sample()))
            .Content.ReadFromJsonAsync<TechniqueResponse>();

        var link = await client.PostAsync($"/api/recipes/{recipe!.Id}/techniques/{technique!.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, link.StatusCode);

        var detail = await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{recipe.Id}");
        Assert.Single(detail!.Techniques);
        Assert.Equal("Braising", detail.Techniques[0].Name);

        var unlink = await client.DeleteAsync($"/api/recipes/{recipe.Id}/techniques/{technique.Id}");
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        var after = await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{recipe.Id}");
        Assert.Empty(after!.Techniques);
    }
}