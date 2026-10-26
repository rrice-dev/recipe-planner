using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Kroger;
using RecipePlanner.Api.Nutrition;

namespace RecipePlanner.Tests;

public class PriceEstimatorTests
{
    [Theory]
    [InlineData("1 lb", 453.592, PriceEstimator.Measure.Mass)]
    [InlineData("16 oz", 453.592, PriceEstimator.Measure.Mass)]
    [InlineData("32 fl oz", 946.352, PriceEstimator.Measure.Volume)]
    [InlineData("1 gal", 3785.41, PriceEstimator.Measure.Volume)]
    [InlineData("12 ct", 12, PriceEstimator.Measure.Count)]
    [InlineData("2 x 14.5 oz", 822.14, PriceEstimator.Measure.Mass)]
    [InlineData("12 ct / 24 oz", 680.388, PriceEstimator.Measure.Mass)]   // weight beats a count
    [InlineData("500 g", 500, PriceEstimator.Measure.Mass)]
    public void Parses_package_sizes(string size, double amount, PriceEstimator.Measure measure)
    {
        var parsed = PriceEstimator.ParseSize(size)!;
        Assert.Equal(measure, parsed.Measure);
        Assert.Equal(amount, parsed.Amount, 0.5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("family size")]
    public void Unknown_sizes_are_null(string? size) => Assert.Null(PriceEstimator.ParseSize(size));

    [Fact]
    public void Prices_the_share_of_a_package_used()
    {
        Assert.Equal(1.75m, PriceEstimator.CostOf(8, "oz", 3.50m, "1 lb", null)!.Value.Cost);          // half a pound
        Assert.Equal(0.50m, PriceEstimator.CostOf(3, "whole", 2.00m, "12 ct", null)!.Value.Cost);       // 3 of 12 eggs
        Assert.Equal(1.00m, PriceEstimator.CostOf(1, "cup", 4.00m, "32 fl oz", null)!.Value.Cost);         // 1/4 quart
        Assert.Equal(2.58m, PriceEstimator.CostOf(2, "can", 1.29m, "15 oz", null)!.Value.Cost);          // 2 whole cans
    }

    [Fact]
    public void Uses_food_weights_to_price_a_cup_from_a_bag_sold_by_weight()
    {
        var flour = new FoodFacts(9, "Flour", new(364, 10, 76, 1, 3, 0, 2), [new("cup", 1, 125)]);
        var (cost, approximate) = PriceEstimator.CostOf(1, "cup", 3.49m, "5 lb", flour)!.Value;
        Assert.Equal(0.19m, cost);     // 125 g of 2,268 g
        Assert.True(approximate);
    }

    [Fact]
    public void Returns_null_when_units_cannot_be_matched()
    {
        Assert.Null(PriceEstimator.CostOf(1, "whole", 3.00m, "1 lb", null));    // no weight for "1 whole" without food data
        Assert.Null(PriceEstimator.CostOf(2, "cup", 3.00m, "12 ct", null));
        Assert.Null(PriceEstimator.CostOf(1, "cup", 3.00m, null, null));
    }
}

public class CostTests
{
    private const string Store = "01400943";

    private static async Task<int> Recipe(HttpClient client, string title, int servings, params (string Name, decimal Qty, string Unit)[] items)
    {
        var recipe = await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, null, null, servings, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var all = await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients");
        var lines = new List<RecipeIngredientRequest>();
        foreach (var (name, qty, unit) in items)
        {
            var id = all!.FirstOrDefault(i => i.Name == name)?.Id
                ?? (await (await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest(name, "Meat")))
                    .Content.ReadFromJsonAsync<IngredientResponse>())!.Id;
            lines.Add(new RecipeIngredientRequest(id, qty, unit, null));
        }
        (await client.PutAsJsonAsync($"/api/recipes/{recipe!.Id}/ingredients", lines)).EnsureSuccessStatusCode();
        return recipe.Id;
    }

    private static async Task<HttpClient> WithStore(ApiFactory factory, FakeKroger kroger)
    {
        var client = await KrogerTests.Connected(factory, kroger);
        (await client.PutAsJsonAsync("/api/kroger/store", new KrogerStoreRequest(Store, "Kroger Montgomery"))).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Recipe_cost_uses_the_store_price_for_the_amount_used()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await WithStore(factory, kroger);
        // The fake store sells everything as "1 lb" for $3.49
        var id = await Recipe(client, "Pork", 4, ("Pork Shoulder", 2, "lb"), ("Mystery Spice", 1, "pinch"));

        var cost = (await client.GetFromJsonAsync<RecipeCostResponse>($"/api/recipes/{id}/cost"))!;

        Assert.Equal("ok", cost.Status);
        Assert.Equal("Kroger Montgomery", cost.StoreName);
        Assert.Equal(6.98m, cost.Total);                // 2 lb at $3.49 a pound; a pinch can't be priced by weight without food data
        Assert.Equal(1.75m, cost.PerServing);
        Assert.Equal((1, 2), (cost.Counted, cost.TotalIngredients));
        var pork = cost.Lines.Single(l => l.Ingredient == "Pork Shoulder");
        Assert.Equal(("Kroger® pork shoulder", "1 lb", 3.49m), (pork.Product, pork.Size, pork.PackagePrice));
    }

    [Fact]
    public async Task Recipe_cost_scales_to_servings_and_prices_are_cached_per_store()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await WithStore(factory, kroger);
        var id = await Recipe(client, "Pork", 4, ("Pork Shoulder", 2, "lb"));

        var eight = (await client.GetFromJsonAsync<RecipeCostResponse>($"/api/recipes/{id}/cost?servings=8"))!;
        Assert.Equal(13.96m, eight.Total);
        Assert.Equal(1.75m, eight.PerServing);

        var searches = kroger.Calls.Count(c => c.Path == "/v1/products");
        await client.GetFromJsonAsync<RecipeCostResponse>($"/api/recipes/{id}/cost");
        Assert.Equal(searches, kroger.Calls.Count(c => c.Path == "/v1/products"));   // second time from the cache
    }

    [Fact]
    public async Task Your_usual_product_is_used_when_you_have_one()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await WithStore(factory, kroger);
        var id = await Recipe(client, "Pork", 4, ("Pork Shoulder", 1, "lb"));
        // Picking the organic one when sending to the cart remembers it
        await client.PostAsJsonAsync("/api/shopping-list/items", new[] { new GroceryAddRequest("Pork Shoulder", 1, "lb", null) });
        var item = (await client.GetFromJsonAsync<List<GroceryItemResponse>>("/api/shopping-list"))!.Single();
        (await client.PostAsJsonAsync("/api/kroger/cart", new KrogerCartRequest("PICKUP",
            [new KrogerCartItem(item.Id, "0001111099999", "Simple Truth Organic™ pork shoulder", 1)]))).EnsureSuccessStatusCode();

        var cost = (await client.GetFromJsonAsync<RecipeCostResponse>($"/api/recipes/{id}/cost"))!;

        Assert.Equal(5.99m, cost.Total);   // 16 oz for $5.99
    }

    [Fact]
    public async Task Says_what_is_missing_before_a_store_is_picked_or_when_kroger_is_off()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var connected = await KrogerTests.Connected(factory, kroger);
        var id = await Recipe(connected, "Pork", 4, ("Pork Shoulder", 1, "lb"));
        Assert.Equal("no-store", (await connected.GetFromJsonAsync<RecipeCostResponse>($"/api/recipes/{id}/cost"))!.Status);

        using var off = new ApiFactory();
        var client = await off.CreateUserClientAsync();
        var id2 = await Recipe(client, "Pork", 4, ("Pork Shoulder", 1, "lb"));
        Assert.Equal("unavailable", (await client.GetFromJsonAsync<RecipeCostResponse>($"/api/recipes/{id2}/cost"))!.Status);
    }

    [Fact]
    public async Task Plan_cost_prices_each_meal_at_its_planned_servings_with_your_budget()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var client = await WithStore(factory, kroger);
        var pork = await Recipe(client, "Pork", 4, ("Pork Shoulder", 2, "lb"));         // $6.98 for 4
        var beans = await Recipe(client, "Beans", 2, ("Black Beans", 1, "can"));         // $3.49 for 2
        var monday = new DateOnly(2026, 10, 26);
        var plan = (await (await client.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Week", monday)))
            .Content.ReadFromJsonAsync<MealPlanResponse>())!;
        await client.PutAsJsonAsync($"/api/mealplans/{plan.Id}/entries", new[]
        {
            new MealPlanEntryRequest(monday, pork, 8),
            new MealPlanEntryRequest(monday.AddDays(1), beans, 2)
        });
        (await client.PutAsJsonAsync("/api/settings/budget", new BudgetSettings(75))).EnsureSuccessStatusCode();

        var cost = (await client.GetFromJsonAsync<PlanCostResponse>($"/api/mealplans/{plan.Id}/cost"))!;

        Assert.Equal([13.96m, 3.49m], cost.Meals.Select(m => m.Cost!.Value));
        Assert.Equal(1.75m, cost.Meals[0].PerServing);
        Assert.Equal(17.45m, cost.Total);
        Assert.Equal(75m, cost.Budget);
        Assert.True(cost.Complete);
    }

    [Fact]
    public async Task Budget_is_validated_and_can_be_cleared()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/settings/budget", new BudgetSettings(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/settings/budget", new BudgetSettings(1_000_000))).StatusCode);
        Assert.Equal(new BudgetSettings(80.13m), await (await client.PutAsJsonAsync("/api/settings/budget", new BudgetSettings(80.125m))).Content.ReadFromJsonAsync<BudgetSettings>());
        Assert.Equal(new BudgetSettings(null), await (await client.PutAsJsonAsync("/api/settings/budget", new BudgetSettings(null))).Content.ReadFromJsonAsync<BudgetSettings>());
    }

    [Fact]
    public async Task Only_the_owner_can_price_a_recipe_or_plan()
    {
        var kroger = new FakeKroger();
        using var factory = new ApiFactory { Kroger = kroger };
        var alice = await WithStore(factory, kroger);
        var bob = await WithStore(factory, kroger);
        var id = await Recipe(alice, "Pork", 4, ("Pork Shoulder", 1, "lb"));
        var plan = (await (await alice.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Week", new DateOnly(2026, 10, 26))))
            .Content.ReadFromJsonAsync<MealPlanResponse>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/recipes/{id}/cost")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/mealplans/{plan.Id}/cost")).StatusCode);
    }
}
