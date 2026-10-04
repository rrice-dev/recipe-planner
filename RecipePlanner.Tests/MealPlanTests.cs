using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class MealPlanTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static async Task<T> Post<T>(HttpClient client, string url, object body) =>
        (await (await client.PostAsJsonAsync(url, body)).Content.ReadFromJsonAsync<T>())!;

    [Fact]
    public async Task Shopping_list_scales_and_combines_ingredients()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var onion = await Post<IngredientResponse>(client, "/api/ingredients", new IngredientRequest("Onion", "Produce"));
        var butter = await Post<IngredientResponse>(client, "/api/ingredients", new IngredientRequest("Butter", "Dairy"));

        // Recipe A serves 4: 2 onions, 1 tbsp butter
        var a = await Post<RecipeResponse>(client, "/api/recipes", new RecipeRequest("Onion Soup", null, null, 4, 10, 60));
        await client.PutAsJsonAsync($"/api/recipes/{a.Id}/ingredients", new[]
        {
            new RecipeIngredientRequest(onion.Id, 2, "whole", null),
            new RecipeIngredientRequest(butter.Id, 1, "tbsp", null)
        });

        // Recipe B serves 2: 1 onion
        var b = await Post<RecipeResponse>(client, "/api/recipes", new RecipeRequest("Onion Rings", null, null, 2, 10, 15));
        await client.PutAsJsonAsync($"/api/recipes/{b.Id}/ingredients", new[]
        {
            new RecipeIngredientRequest(onion.Id, 1, "whole", null)
        });

        var plan = await Post<MealPlanResponse>(client, "/api/mealplans", new MealPlanRequest("Test Week", Monday));

        // A planned for 8 servings (double), B for 2 servings (as written)
        var set = await client.PutAsJsonAsync($"/api/mealplans/{plan.Id}/entries", new[]
        {
            new MealPlanEntryRequest(Monday, a.Id, 8),
            new MealPlanEntryRequest(Monday.AddDays(2), b.Id, 2)
        });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var list = await client.GetFromJsonAsync<ShoppingListResponse>($"/api/mealplans/{plan.Id}/shopping-list");

        var onions = Assert.Single(list!.Items, i => i.Ingredient == "Onion");
        Assert.Equal(5m, onions.Quantity); // 2 x 2 + 1 = 5

        var butterItem = Assert.Single(list.Items, i => i.Ingredient == "Butter");
        Assert.Equal(2m, butterItem.Quantity); // 1 x 2 = 2
    }

    [Fact]
    public async Task Entry_outside_the_week_returns_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var recipe = await Post<RecipeResponse>(client, "/api/recipes", new RecipeRequest("Soup", null, null, 4, 10, 30));
        var plan = await Post<MealPlanResponse>(client, "/api/mealplans", new MealPlanRequest("Week", Monday));

        var response = await client.PutAsJsonAsync($"/api/mealplans/{plan.Id}/entries",
            new[] { new MealPlanEntryRequest(Monday.AddDays(10), recipe.Id, 4) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Entry_with_unknown_recipe_returns_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var plan = await Post<MealPlanResponse>(client, "/api/mealplans", new MealPlanRequest("Week", Monday));
        var response = await client.PutAsJsonAsync($"/api/mealplans/{plan.Id}/entries",
            new[] { new MealPlanEntryRequest(Monday, 9999, 4) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Shopping_list_for_missing_plan_returns_404()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/mealplans/9999/shopping-list");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}