using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Nutrition;

namespace RecipePlanner.Tests;

public class RecipeCaloriesTests
{
    private static async Task<(ApiFactory Factory, HttpClient Client, List<IngredientResponse> Ingredients)> Setup(FakeFoods? foods)
    {
        var factory = new ApiFactory { Foods = foods };
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        var client = await factory.CreateUserClientAsync();
        var all = (await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients"))!;
        return (factory, client, all);
    }

    private static async Task<int> Recipe(HttpClient client, string title, int servings, params RecipeIngredientRequest[] lines)
    {
        var id = (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, null, null, servings, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;
        if (lines.Length > 0) (await client.PutAsJsonAsync($"/api/recipes/{id}/ingredients", lines)).EnsureSuccessStatusCode();
        return id;
    }

    [Fact]
    public async Task Lists_calories_per_serving_for_each_recipe()
    {
        var (factory, client, all) = await Setup(new FakeFoods());
        using var _ = factory;
        int Ing(string name) => all.Single(i => i.Name == name).Id;

        var chicken = await Recipe(client, "Chicken", 2, new RecipeIngredientRequest(Ing("Chicken Thighs"), 1, "lb", null));
        var oily = await Recipe(client, "Saffron oil", 1,
            new RecipeIngredientRequest(Ing("Olive Oil"), 2, "tbsp", null), new RecipeIngredientRequest(Ing("Saffron"), 1, "pinch", null));
        var empty = await Recipe(client, "Nothing yet", 2);

        var r = (await client.GetFromJsonAsync<RecipeCaloriesResponse>("/api/recipes/calories"))!;

        Assert.True(r.Available);
        Assert.False(r.Pending);
        var byId = r.Recipes.ToDictionary(x => x.RecipeId);
        Assert.InRange(byId[chicken].Calories!.Value, 272, 276);   // 549 kcal over 2 servings
        Assert.True(byId[chicken].Complete);
        Assert.InRange(byId[oily].Calories!.Value, 237, 241);
        Assert.False(byId[oily].Complete);                          // saffron couldn't be counted
        Assert.Null(byId[empty].Calories);
        Assert.False(byId[empty].Complete);
    }

    [Fact]
    public async Task Only_your_own_recipes_are_listed()
    {
        var (factory, client, all) = await Setup(new FakeFoods());
        using var _ = factory;
        await Recipe(client, "Mine", 2, new RecipeIngredientRequest(all.Single(i => i.Name == "Chicken Thighs").Id, 1, "lb", null));

        var other = await factory.CreateUserClientAsync();
        var r = (await other.GetFromJsonAsync<RecipeCaloriesResponse>("/api/recipes/calories"))!;
        Assert.Empty(r.Recipes);
    }

    [Fact]
    public async Task Many_new_ingredients_are_looked_up_over_several_calls()
    {
        var foods = new FakeFoods();
        var (factory, client, all) = await Setup(foods);
        using var _ = factory;
        var lines = all.Take(NutritionService.MaxLookupsPerRequest + 5).Select(i => new RecipeIngredientRequest(i.Id, 1, "g", null)).ToArray();
        Assert.True(lines.Length > NutritionService.MaxLookupsPerRequest, "need more seeded ingredients than one call looks up");
        await Recipe(client, "Everything", 1, lines);

        var first = (await client.GetFromJsonAsync<RecipeCaloriesResponse>("/api/recipes/calories"))!;
        Assert.True(first.Pending);
        var second = (await client.GetFromJsonAsync<RecipeCaloriesResponse>("/api/recipes/calories"))!;
        Assert.False(second.Pending);
        Assert.All(foods.Lookups.Values, n => Assert.Equal(1, n));    // each name looked up once, then cached
    }

    [Fact]
    public async Task Says_unavailable_without_a_usda_key()
    {
        var (factory, client, _) = await Setup(null);
        using var __ = factory;
        var r = (await client.GetFromJsonAsync<RecipeCaloriesResponse>("/api/recipes/calories"))!;
        Assert.False(r.Available);
        Assert.Empty(r.Recipes);
    }
}
