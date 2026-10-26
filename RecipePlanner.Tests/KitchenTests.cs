using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Kitchen;

namespace RecipePlanner.Tests;

public class KitchenTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<int> Recipe(HttpClient client, string title, int servings, params (string Name, decimal Qty, string Unit)[] items)
    {
        var recipe = await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, null, null, servings, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var all = await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients");
        var lines = new List<RecipeIngredientRequest>();
        foreach (var (name, qty, unit) in items)
        {
            var id = all!.FirstOrDefault(i => i.Name == name)?.Id
                ?? (await (await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest(name, "Pantry")))
                    .Content.ReadFromJsonAsync<IngredientResponse>())!.Id;
            lines.Add(new RecipeIngredientRequest(id, qty, unit, null));
        }
        (await client.PutAsJsonAsync($"/api/recipes/{recipe!.Id}/ingredients", lines)).EnsureSuccessStatusCode();
        return recipe.Id;
    }

    private static async Task<LeftoverResponse> AddLeftover(HttpClient client, LeftoverRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/leftovers", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LeftoverResponse>())!;
    }

    [Theory]
    [InlineData("/api/pantry")]
    [InlineData("/api/leftovers")]
    public async Task Requires_login(string url)
    {
        using var factory = new ApiFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Pantry_ignores_duplicates_in_any_case_and_sorts_by_name()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["Salt", "olive  oil"]));
        var response = await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["SALT", "Rice", "rice"]));

        var pantry = (await response.Content.ReadFromJsonAsync<List<PantryItemResponse>>())!;
        Assert.Equal(["olive  oil", "Rice", "Salt"], pantry.Select(p => p.Name));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pantry/{pantry[0].Id}")).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<List<PantryItemResponse>>("/api/pantry"))!.Count);
    }

    [Fact]
    public async Task Pantry_rejects_blank_or_too_many_names()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest([" "]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest([]))).StatusCode);
        var tooMany = Enumerable.Range(0, 51).Select(i => $"Item {i}").ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(tooMany))).StatusCode);
    }

    [Fact]
    public async Task Adding_a_recipe_to_the_list_skips_pantry_staples_and_says_which()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var id = await Recipe(client, "Pasta", 2, ("Spaghetti", 8, "oz"), ("Kosher Salt", 1, "tsp"), ("Olive Oil", 2, "tbsp"));
        await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["kosher salt", "Olive oil"]));

        var result = await (await client.PostAsJsonAsync($"/api/shopping-list/from-recipe/{id}", new GroceryFromRecipeRequest(null)))
            .Content.ReadFromJsonAsync<GroceryAddResult>();

        Assert.Equal(["Spaghetti"], result!.Items.Select(i => i.Name));
        Assert.Equal(["Kosher Salt", "Olive Oil"], result.SkippedPantry);
    }

    [Fact]
    public async Task Pantry_staples_can_be_included_on_purpose()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var id = await Recipe(client, "Rice", 2, ("Jasmine Rice", 1, "cup"));
        await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["Jasmine Rice"]));

        var result = await (await client.PostAsJsonAsync($"/api/shopping-list/from-recipe/{id}?includePantry=true", new GroceryFromRecipeRequest(null)))
            .Content.ReadFromJsonAsync<GroceryAddResult>();

        Assert.Single(result!.Items);
        Assert.Empty(result.SkippedPantry);
    }

    [Fact]
    public async Task Meal_plans_skip_pantry_staples_and_the_plan_list_marks_them()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var id = await Recipe(client, "Soup", 4, ("Onion", 1, "whole"), ("Salt", 1, "tsp"));
        await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["salt"]));
        var plan = await (await client.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Week", Today)))
            .Content.ReadFromJsonAsync<MealPlanResponse>();
        await client.PutAsJsonAsync($"/api/mealplans/{plan!.Id}/entries", new[] { new MealPlanEntryRequest(Today, id, 4) });

        var result = await (await client.PostAsync($"/api/shopping-list/from-meal-plan/{plan.Id}", null))
            .Content.ReadFromJsonAsync<GroceryAddResult>();
        Assert.Equal(["Onion"], result!.Items.Select(i => i.Name));
        Assert.Equal(["Salt"], result.SkippedPantry);

        var planList = await client.GetFromJsonAsync<ShoppingListResponse>($"/api/mealplans/{plan.Id}/shopping-list");
        Assert.True(planList!.Items.Single(i => i.Ingredient == "Salt").InPantry);
        Assert.False(planList.Items.Single(i => i.Ingredient == "Onion").InPantry);
    }

    [Fact]
    public async Task Typed_items_are_never_skipped_even_if_in_the_pantry()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        await client.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["Salt"]));

        await client.PostAsJsonAsync("/api/shopping-list/items", new[] { new GroceryAddRequest("Salt", null, null, null) });

        Assert.Single((await client.GetFromJsonAsync<List<GroceryItemResponse>>("/api/shopping-list"))!);
    }

    [Fact]
    public async Task Leftovers_get_a_use_by_date_from_where_they_are_kept()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var chili = await AddLeftover(client, new LeftoverRequest("Chili", null, 4, "fridge", Today));
        var soup = await AddLeftover(client, new LeftoverRequest("Soup", null, 2, "freezer", Today));

        Assert.Equal(Today.AddDays(KitchenRules.FridgeDays), chili.UseBy);
        Assert.Equal(Today.AddDays(KitchenRules.FreezerDays), soup.UseBy);
        var list = await client.GetFromJsonAsync<List<LeftoverResponse>>("/api/leftovers");
        Assert.Equal(["Chili", "Soup"], list!.Select(l => l.Name));   // soonest first
    }

    [Fact]
    public async Task Leftovers_from_a_recipe_take_its_title_and_only_your_own_recipes()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var id = await Recipe(alice, "Lemon Chicken", 4);

        var leftover = await AddLeftover(alice, new LeftoverRequest(null, id, 3, "fridge", null));
        Assert.Equal("Lemon Chicken", leftover.Name);
        Assert.Equal(id, leftover.RecipeId);

        var stolen = await bob.PostAsJsonAsync("/api/leftovers", new LeftoverRequest(null, id, 3, "fridge", null));
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Theory]
    [InlineData("", 2, "fridge", 0)]           // no name
    [InlineData("Chili", 0, "fridge", 0)]      // no portions
    [InlineData("Chili", 51, "fridge", 0)]     // too many
    [InlineData("Chili", 2, "counter", 0)]     // not a place
    [InlineData("Chili", 2, "fridge", -10)]    // too old for the fridge
    [InlineData("Chili", 2, "fridge", 5)]      // in the future
    public async Task Invalid_leftovers_are_rejected(string name, int portions, string location, int daysFromToday)
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var response = await client.PostAsJsonAsync("/api/leftovers",
            new LeftoverRequest(name, null, portions, location, Today.AddDays(daysFromToday)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Old_food_can_be_logged_in_the_freezer()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var stew = await AddLeftover(client, new LeftoverRequest("Stew", null, 2, "freezer", Today.AddDays(-30)));
        Assert.Equal(Today.AddDays(60), stew.UseBy);
    }

    [Fact]
    public async Task Eating_counts_down_and_removes_it_when_gone()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var chili = await AddLeftover(client, new LeftoverRequest("Chili", null, 2, "fridge", null));

        var after = await (await client.PostAsJsonAsync($"/api/leftovers/{chili.Id}/eat", new LeftoverEatRequest(null)))
            .Content.ReadFromJsonAsync<LeftoverResponse>();
        Assert.Equal(1, after!.Portions);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/leftovers/{chili.Id}/eat", new LeftoverEatRequest(1))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<LeftoverResponse>>("/api/leftovers"))!);
    }

    [Fact]
    public async Task Freezing_restarts_the_clock_and_thawing_gives_fridge_days()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var chili = await AddLeftover(client, new LeftoverRequest("Chili", null, 4, "fridge", Today.AddDays(-2)));

        var frozen = await (await client.PatchAsJsonAsync($"/api/leftovers/{chili.Id}", new LeftoverUpdateRequest(null, "freezer", Today)))
            .Content.ReadFromJsonAsync<LeftoverResponse>();
        Assert.Equal("freezer", frozen!.Location);
        Assert.Equal(Today.AddDays(KitchenRules.FreezerDays), frozen.UseBy);

        var thawing = await (await client.PatchAsJsonAsync($"/api/leftovers/{chili.Id}", new LeftoverUpdateRequest(null, "fridge", Today)))
            .Content.ReadFromJsonAsync<LeftoverResponse>();
        Assert.Equal(Today.AddDays(KitchenRules.FridgeDays), thawing!.UseBy);
        Assert.Equal(Today.AddDays(-2), thawing.MadeOn);
    }

    [Fact]
    public async Task Food_past_its_fridge_date_cannot_be_frozen()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        // Made 4 days ago: use-by is today, so freezing tomorrow (allowed time zone slack) is too late
        var chili = await AddLeftover(client, new LeftoverRequest("Chili", null, 4, "fridge", Today.AddDays(-4)));

        var response = await client.PatchAsJsonAsync($"/api/leftovers/{chili.Id}", new LeftoverUpdateRequest(null, "freezer", Today.AddDays(1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_or_change_each_others_kitchen()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var chili = await AddLeftover(alice, new LeftoverRequest("Chili", null, 2, "fridge", null));
        var pantry = await (await alice.PostAsJsonAsync("/api/pantry", new PantryAddRequest(["Salt"])))
            .Content.ReadFromJsonAsync<List<PantryItemResponse>>();

        Assert.Empty((await bob.GetFromJsonAsync<List<LeftoverResponse>>("/api/leftovers"))!);
        Assert.Empty((await bob.GetFromJsonAsync<List<PantryItemResponse>>("/api/pantry"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/leftovers/{chili.Id}/eat", new LeftoverEatRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PatchAsJsonAsync($"/api/leftovers/{chili.Id}", new LeftoverUpdateRequest(9, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/leftovers/{chili.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/pantry/{pantry![0].Id}")).StatusCode);
    }
}
