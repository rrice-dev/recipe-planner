using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Nutrition;

namespace RecipePlanner.Tests;

// Nutrition facts and store prices are cached in shared tables, one row per ingredient name. Two requests
// looking up the same new name at the same moment used to both try to save that row, and the second failed
// with a duplicate key (a 500). Both must succeed, and the row must be saved once.
public class CacheRaceTests
{
    private sealed class SlowFoods : IFoodSource
    {
        public bool IsAvailable => true;
        public async Task<FoodFacts?> FindAsync(string ingredient, CancellationToken ct)
        {
            await Task.Delay(500, ct); // long enough for both requests to be looking it up at once
            return FakeFoods.Known.GetValueOrDefault(ingredient);
        }
    }

    [Fact]
    public async Task Two_nutrition_lookups_of_the_same_new_ingredient_at_once_both_succeed()
    {
        using var factory = new ApiFactory { Foods = new SlowFoods() };
        var client = factory.CreateClient();
        var slug = (await client.GetFromJsonAsync<List<LibrarySummaryResponse>>("/api/library"))![0].Slug;

        var responses = await Task.WhenAll(
            client.GetAsync($"/api/library/{slug}/nutrition"),
            client.GetAsync($"/api/library/{slug}/nutrition"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        using var scope = factory.Services.CreateScope();
        var keys = await scope.ServiceProvider.GetRequiredService<AppDbContext>().IngredientNutrition.Select(n => n.NameKey).ToListAsync();
        Assert.NotEmpty(keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public async Task Two_cost_estimates_of_the_same_new_ingredient_at_once_both_succeed()
    {
        var kroger = new FakeKroger { ProductDelay = TimeSpan.FromMilliseconds(500) };
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await KrogerTests.Connected(factory, kroger);
        (await client.PutAsJsonAsync("/api/kroger/store", new KrogerStoreRequest("01400943", "Kroger Montgomery"))).EnsureSuccessStatusCode();
        var recipe = (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Pork", null, null, 4, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;
        var pork = (await (await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest("Pork Shoulder", "Meat")))
            .Content.ReadFromJsonAsync<IngredientResponse>())!.Id;
        (await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients", new[] { new RecipeIngredientRequest(pork, 2, "lb", null) })).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(
            client.GetAsync($"/api/recipes/{recipe}/cost"),
            client.GetAsync($"/api/recipes/{recipe}/cost"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(6.98m, r.Content.ReadFromJsonAsync<RecipeCostResponse>().Result!.Total));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().IngredientPrices.CountAsync());
    }
}
