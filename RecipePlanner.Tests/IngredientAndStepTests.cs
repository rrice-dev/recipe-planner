using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class IngredientAndStepTests
{
    private static async Task<RecipeResponse> CreateRecipe(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Caramelized Onions", null, "French", 4, 10, 45)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!;

    private static async Task<IngredientResponse> CreateIngredient(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/ingredients",
            new IngredientRequest(name, "Produce")))
            .Content.ReadFromJsonAsync<IngredientResponse>())!;

    [Fact]
    public async Task Duplicate_ingredient_returns_409()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        await CreateIngredient(client, "Onion");
        var duplicate = await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest("Onion", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Recipe_ingredients_appear_in_detail_and_can_be_replaced()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var recipe = await CreateRecipe(client);
        var onion = await CreateIngredient(client, "Onion");
        var butter = await CreateIngredient(client, "Butter");

        var set = await client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/ingredients", new[]
        {
            new RecipeIngredientRequest(onion.Id, 4, "whole", "thinly sliced"),
            new RecipeIngredientRequest(butter.Id, 2, "tbsp", null)
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var detail = await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{recipe.Id}");
        Assert.Equal(2, detail!.Ingredients.Count);

        // Replace with just onions, at a new quantity
        await client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/ingredients", new[]
        {
            new RecipeIngredientRequest(onion.Id, 6, "whole", null)
        });

        var after = await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{recipe.Id}");
        Assert.Single(after!.Ingredients);
        Assert.Equal(6, after.Ingredients[0].Quantity);
    }

    [Fact]
    public async Task Deleting_ingredient_in_use_returns_409()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var recipe = await CreateRecipe(client);
        var onion = await CreateIngredient(client, "Onion");
        await client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/ingredients",
            new[] { new RecipeIngredientRequest(onion.Id, 4, "whole", null) });

        var delete = await client.DeleteAsync($"/api/ingredients/{onion.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Steps_are_saved_in_order_with_technique_names()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var recipe = await CreateRecipe(client);
        var technique = (await (await client.PostAsJsonAsync("/api/techniques",
            new TechniqueRequest("Caramelizing", "Cook sugars slowly until deep brown.", "Deep amber color", "Heat too high", 2)))
            .Content.ReadFromJsonAsync<TechniqueResponse>())!;

        var set = await client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/steps", new[]
        {
            new StepRequest("Slice the onions thinly.", null),
            new StepRequest("Cook over low heat for 45 minutes, stirring occasionally.", technique.Id)
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var detail = await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{recipe.Id}");
        Assert.Equal(2, detail!.Steps.Count);
        Assert.Equal(1, detail.Steps[0].Order);
        Assert.Equal("Caramelizing", detail.Steps[1].TechniqueName);
    }

    [Fact]
    public async Task Step_with_unknown_technique_returns_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var recipe = await CreateRecipe(client);
        var response = await client.PutAsJsonAsync($"/api/recipes/{recipe.Id}/steps",
            new[] { new StepRequest("Do something.", 9999) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}