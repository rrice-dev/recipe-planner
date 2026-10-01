using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class RecipeEndpointTests
{
    private static RecipeRequest Sample(string title = "Braised Short Ribs") =>
        new(title, "Slow braised and tender", "Korean", 4, 20, 180);

    [Fact]
    public async Task Create_then_get_returns_recipe()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/recipes", Sample());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<RecipeResponse>();

        var fetched = await client.GetFromJsonAsync<RecipeResponse>($"/api/recipes/{created!.Id}");
        Assert.Equal("Braised Short Ribs", fetched!.Title);
    }

    [Fact]
    public async Task Create_with_blank_title_returns_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/recipes", Sample(title: "  "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_missing_recipe_returns_404()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/recipes/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_changes_recipe()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/recipes", Sample());
        var created = await create.Content.ReadFromJsonAsync<RecipeResponse>();

        var update = await client.PutAsJsonAsync($"/api/recipes/{created!.Id}", Sample("Galbi-jjim"));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var fetched = await client.GetFromJsonAsync<RecipeResponse>($"/api/recipes/{created.Id}");
        Assert.Equal("Galbi-jjim", fetched!.Title);
    }

    [Fact]
    public async Task Delete_removes_recipe()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/recipes", Sample());
        var created = await create.Content.ReadFromJsonAsync<RecipeResponse>();

        var delete = await client.DeleteAsync($"/api/recipes/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var get = await client.GetAsync($"/api/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }
}