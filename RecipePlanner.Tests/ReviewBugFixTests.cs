using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

// The five bugs from the Oct 29 code review. Each test failed before its fix.
public class ReviewBugFixTests
{
    private static async Task<int> NewRecipe(HttpClient client, string? description = null) =>
        (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", description, null, 2, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;

    // Test databases start empty, so make what each test needs
    private static async Task<int> AnyIngredientId(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest($"Leek {Guid.NewGuid():N}", null)))
            .Content.ReadFromJsonAsync<IngredientResponse>())!.Id;

    private static async Task<TechniqueResponse> NewTechnique(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/techniques", new TechniqueRequest($"Braise {Guid.NewGuid():N}", "Low and slow", null, null, 2)))
            .Content.ReadFromJsonAsync<TechniqueResponse>())!;

    // ---------- 1. Deleting a technique a recipe step uses ----------

    [Fact]
    public async Task A_technique_used_by_a_recipe_step_cant_be_deleted()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateUserClientAsync();
        var admin = await factory.CreateAdminClientAsync();
        var technique = await NewTechnique(admin);
        var recipe = await NewRecipe(user);
        (await user.PutAsJsonAsync($"/api/recipes/{recipe}/steps", new[] { new StepRequest("Do the thing", technique.Id) })).EnsureSuccessStatusCode();

        var response = await admin.DeleteAsync($"/api/techniques/{technique.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/techniques/{technique.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_unused_technique_can_still_be_deleted()
    {
        using var factory = new ApiFactory();
        var admin = await factory.CreateAdminClientAsync();
        var technique = await NewTechnique(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/techniques/{technique.Id}")).StatusCode);
    }

    // ---------- 2. Ingredient amounts outside what the database holds ----------

    [Theory]
    [InlineData(100_000_000)]   // too large for the column: was a 500 on Postgres
    [InlineData(10_000.01)]
    [InlineData(0.001)]         // was saved as 0
    public async Task Ingredient_amounts_out_of_range_are_refused(double quantity)
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var recipe = await NewRecipe(client);
        var response = await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients",
            new[] { new RecipeIngredientRequest(await AnyIngredientId(client), (decimal)quantity, "g", null) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(10_000)]
    public async Task Ingredient_amounts_at_the_edges_are_fine(double quantity)
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var recipe = await NewRecipe(client);
        var response = await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients",
            new[] { new RecipeIngredientRequest(await AnyIngredientId(client), (decimal)quantity, "g", null) });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Ai_drafts_stay_within_what_the_recipe_editor_accepts()
    {
        var raw = new RecipePlanner.Api.Ai.RawRecipe("Soup", null, 2, 5, 20,
            [new("Salt", 0.001m, "teaspoons, finely ground sea", null)], [new("Stir", null)]);
        var draft = RecipePlanner.Api.Ai.FridgePlanner.CleanRecipe(raw, [], [])!;
        var salt = Assert.Single(draft.Ingredients);
        Assert.Equal(0.01m, salt.Quantity);
        Assert.True(salt.Unit.Length <= 20, $"unit was {salt.Unit.Length} characters");

        // And that draft saves
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var recipe = await NewRecipe(client);
        var saved = await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients",
            new[] { new RecipeIngredientRequest(await AnyIngredientId(client), salt.Quantity, salt.Unit, salt.Note) });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    }

    // ---------- 3. Meal plan servings with no upper limit ----------

    [Fact]
    public async Task Meal_plan_servings_are_capped()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var recipe = await NewRecipe(client);
        var week = new DateOnly(2026, 11, 2);
        var plan = (await (await client.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Week", week)))
            .Content.ReadFromJsonAsync<MealPlanResponse>())!.Id;

        var huge = await client.PutAsJsonAsync($"/api/mealplans/{plan}/entries", new[] { new MealPlanEntryRequest(week, recipe, 2_000_000_000) });
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);

        var tooMany = Enumerable.Range(0, 51).Select(_ => new MealPlanEntryRequest(week, recipe, 2)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/mealplans/{plan}/entries", tooMany)).StatusCode);

        var fine = await client.PutAsJsonAsync($"/api/mealplans/{plan}/entries", new[] { new MealPlanEntryRequest(week, recipe, 100) });
        Assert.Equal(HttpStatusCode.NoContent, fine.StatusCode);
    }

    // ---------- 4. A malformed AI reply ----------

    [Fact]
    public async Task Fridge_suggestions_still_show_your_matches_when_the_ai_reply_is_malformed()
    {
        using var factory = new ApiFactory { MealIdeas = new FakeMealIdeas { ThrowThis = new JsonException("bad shape") } };
        var client = await factory.CreateUserClientAsync();
        var response = await client.PostAsJsonAsync("/api/fridge/suggestions", new FridgeRequest(["eggs"]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<FridgeResponse>())!.AiNotice);
    }

    [Fact]
    public async Task A_photo_scan_with_a_malformed_ai_reply_says_unavailable()
    {
        using var factory = new ApiFactory { Scanner = new FakeIngredientScanner { ThrowThis = new JsonException("bad shape") } };
        var client = await factory.CreateUserClientAsync();
        var response = await client.PostAsync("/api/fridge/scan",
            new ByteArrayContent(TestImages.Jpeg(64, 64)) { Headers = { ContentType = new("image/jpeg") } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---------- 5. No length limits on recipe text ----------

    [Fact]
    public async Task Oversized_recipe_text_is_refused()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var big = new string('x', 2001);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", big, null, 2, 5, 20))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", null, new string('x', 61), 2, 5, 20))).StatusCode);

        var recipe = await NewRecipe(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/recipes/{recipe}/steps", new[] { new StepRequest(big, null) })).StatusCode);
        var tooManySteps = Enumerable.Range(0, 101).Select(i => new StepRequest($"Step {i}", null)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/recipes/{recipe}/steps", tooManySteps)).StatusCode);

        var ingredient = await AnyIngredientId(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients",
            new[] { new RecipeIngredientRequest(ingredient, 1, new string('x', 21), null) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients",
            new[] { new RecipeIngredientRequest(ingredient, 1, "g", new string('x', 201)) })).StatusCode);
    }

    [Fact]
    public async Task Recipe_text_at_the_limits_is_fine()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var created = await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", new string('x', 2000), new string('x', 60), 2, 5, 20));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = (await created.Content.ReadFromJsonAsync<RecipeResponse>())!.Id;

        var steps = Enumerable.Range(0, 100).Select(_ => new StepRequest(new string('x', 2000), null)).ToArray();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/recipes/{recipe}/steps", steps)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/recipes/{recipe}/ingredients",
            new[] { new RecipeIngredientRequest(await AnyIngredientId(client), 1, new string('x', 20), new string('x', 200)) })).StatusCode);
    }
}
