using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Endpoints;

namespace RecipePlanner.Tests;

public class GroceryListTests
{
    private const string Url = "/api/shopping-list";

    private static async Task<int> Ingredient(HttpClient client, string name, string section)
    {
        var all = await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients");
        return all!.FirstOrDefault(i => i.Name == name)?.Id
            ?? (await (await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest(name, section)))
                .Content.ReadFromJsonAsync<IngredientResponse>())!.Id;
    }

    private static async Task<int> Recipe(HttpClient client, string title, int servings, params (string Name, string Section, decimal Qty, string Unit)[] items)
    {
        var recipe = await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, null, null, servings, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var lines = new List<RecipeIngredientRequest>();
        foreach (var i in items)
            lines.Add(new RecipeIngredientRequest(await Ingredient(client, i.Name, i.Section), i.Qty, i.Unit, null));
        (await client.PutAsJsonAsync($"/api/recipes/{recipe!.Id}/ingredients", lines)).EnsureSuccessStatusCode();
        return recipe.Id;
    }

    private static async Task<List<GroceryItemResponse>> List(HttpClient client) =>
        (await client.GetFromJsonAsync<List<GroceryItemResponse>>(Url))!;

    [Fact]
    public async Task Requires_login()
    {
        using var factory = new ApiFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Adding_two_recipes_combines_the_same_ingredient_and_unit()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var soup = await Recipe(client, "Onion Soup", 4, ("Onion", "Produce", 3, "whole"), ("Butter", "Dairy", 2, "tbsp"));
        var tacos = await Recipe(client, "Tacos", 4, ("Onion", "Produce", 1, "Whole"), ("Butter", "Dairy", 1, "tablespoons"));

        await client.PostAsJsonAsync($"{Url}/from-recipe/{soup}", new GroceryFromRecipeRequest(null));
        await client.PostAsJsonAsync($"{Url}/from-recipe/{tacos}", new GroceryFromRecipeRequest(null));

        var list = await List(client);
        var onion = Assert.Single(list, i => i.Name == "Onion");
        Assert.Equal(4, onion.Quantity);
        Assert.Equal("whole", onion.Unit);
        Assert.Equal("Produce", onion.StoreSection);
        Assert.Equal("Onion Soup, Tacos", onion.Sources);
        var butter = Assert.Single(list, i => i.Name == "Butter");
        Assert.Equal(3, butter.Quantity);                    // "tbsp" + "tablespoons" combine
    }

    [Fact]
    public async Task Recipes_scale_to_the_servings_you_choose()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var id = await Recipe(client, "Rice", 2, ("Jasmine Rice", "Grains & Pasta", 1, "cup"));

        await client.PostAsJsonAsync($"{Url}/from-recipe/{id}", new GroceryFromRecipeRequest(6));

        Assert.Equal(3, (await List(client)).Single().Quantity);
    }

    [Fact]
    public async Task Meal_plans_add_every_recipe_scaled_to_planned_servings()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var rice = await Recipe(client, "Rice", 2, ("Jasmine Rice", "Grains & Pasta", 1, "cup"));
        var plan = await (await client.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Week", new DateOnly(2026, 10, 26))))
            .Content.ReadFromJsonAsync<MealPlanResponse>();
        await client.PutAsJsonAsync($"/api/mealplans/{plan!.Id}/entries", new[]
        {
            new MealPlanEntryRequest(new DateOnly(2026, 10, 26), rice, 4),
            new MealPlanEntryRequest(new DateOnly(2026, 10, 27), rice, 2)
        });

        await client.PostAsync($"{Url}/from-meal-plan/{plan.Id}", null);

        var item = Assert.Single(await List(client));
        Assert.Equal(3, item.Quantity);                      // 2 cups + 1 cup
        Assert.Equal("Week", item.Sources);
    }

    [Fact]
    public async Task Typed_items_link_to_known_ingredients_or_stay_free_text()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await Ingredient(client, "Cilantro", "Produce");

        var response = await client.PostAsJsonAsync($"{Url}/items", new[]
        {
            new GroceryAddRequest("cilantro", null, null, "What can I make?"),
            new GroceryAddRequest("Paper towels", 1, "pack", null)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await List(client);
        Assert.Equal("Produce", list.Single(i => i.Name == "Cilantro").StoreSection);   // matched, canonical name
        Assert.Null(list.Single(i => i.Name == "Paper towels").StoreSection);           // free text
    }

    [Fact]
    public async Task Checked_items_are_not_merged_into_and_can_be_cleared()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Milk", 1, "gallon", null) });
        var milk = (await List(client)).Single();

        await client.PatchAsJsonAsync($"{Url}/items/{milk.Id}", new GroceryUpdateRequest(true, null));
        await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Milk", 1, "gallon", null) });

        var list = await List(client);
        Assert.Equal(2, list.Count);                         // one already in the cart, one still needed
        Assert.False(list[0].Checked);                        // unchecked items sort first

        await client.DeleteAsync($"{Url}?checkedOnly=true");
        Assert.Single(await List(client));
        await client.DeleteAsync(Url);
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Users_cannot_see_or_change_each_others_lists()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        await alice.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Eggs", 12, "whole", null) });
        var item = (await List(alice)).Single();
        var aliceRecipe = await Recipe(alice, "Secret", 2, ("Eggs", "Dairy", 2, "whole"));

        Assert.Empty(await List(bob));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PatchAsJsonAsync($"{Url}/items/{item.Id}", new GroceryUpdateRequest(true, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"{Url}/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"{Url}/from-recipe/{aliceRecipe}", new GroceryFromRecipeRequest(null))).StatusCode);
        await bob.DeleteAsync(Url);
        Assert.Single(await List(alice));                    // Bob clearing his list didn't touch Alice's
    }

    [Fact]
    public async Task Items_without_amounts_join_existing_lines_instead_of_duplicating()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var recipe = await Recipe(client, "Lemon Chicken", 4, ("Lemon", "Produce", 2, "whole"));
        await client.PostAsJsonAsync($"{Url}/from-recipe/{recipe}", new GroceryFromRecipeRequest(null));

        // "Still need: Lemon" from the fridge planner has no amount
        await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Lemon", null, null, "What can I make?") });
        var lemon = Assert.Single(await List(client));
        Assert.Equal(2, lemon.Quantity);
        Assert.Equal("Lemon Chicken, What can I make?", lemon.Sources);

        // And the other way round: a bare "Garlic" gets its amount filled in later
        await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Garlic", null, null, null) });
        await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Garlic", 3, "cloves", null) });
        var garlic = Assert.Single(await List(client), i => i.Name == "Garlic");
        Assert.Equal(3, garlic.Quantity);
        Assert.Equal("clove", garlic.Unit);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Eggs", 0)]
    [InlineData("Eggs", -3)]
    [InlineData("Eggs", 999999)]
    public async Task Rejects_bad_items(string name, int quantity)
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var response = await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest(name, quantity, null, null) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_size_is_capped()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        for (var batch = 0; batch < GroceryListEndpoints.MaxItems / GroceryListEndpoints.MaxBatch; batch++)
        {
            var items = Enumerable.Range(0, GroceryListEndpoints.MaxBatch)
                .Select(i => new GroceryAddRequest($"Item {batch}-{i}", 1, null, null)).ToArray();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{Url}/items", items)).StatusCode);
        }
        var oneMore = await client.PostAsJsonAsync($"{Url}/items", new[] { new GroceryAddRequest("Straw that breaks", 1, null, null) });
        Assert.Equal(HttpStatusCode.BadRequest, oneMore.StatusCode);
    }

    [Theory]
    [InlineData("Tablespoons", "tbsp")]
    [InlineData("tsp.", "tsp")]
    [InlineData("Pounds", "lb")]
    [InlineData("each", "whole")]
    [InlineData("  ", null)]
    [InlineData("pinch", "pinch")]
    public void Units_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, GroceryListEndpoints.NormalizeUnit(input));
}
