using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class AuthTests
{
    private static RecipeRequest Sample(string title = "Alice's Secret Stew") =>
        new(title, null, null, 4, 10, 60);

    [Fact]
    public async Task Recipes_require_login()
    {
        using var factory = new ApiFactory();
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/recipes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Techniques_are_readable_without_login()
    {
        using var factory = new ApiFactory();
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/techniques");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_technique_requires_login()
    {
        using var factory = new ApiFactory();
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/techniques",
            new TechniqueRequest("Sneaky", "Should not be allowed.", null, null, 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_access_each_others_recipes()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync("alice@test.local");
        var bob = await factory.CreateUserClientAsync("bob@test.local");

        var created = await (await alice.PostAsJsonAsync("/api/recipes", Sample()))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var url = $"/api/recipes/{created!.Id}";

        // Bob tries to read, edit, and delete Alice's recipe by guessing the ID
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(url, Sample("Hacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(url)).StatusCode);

        // Bob's list doesn't include it
        var bobsRecipes = await bob.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes");
        Assert.Empty(bobsRecipes!);

        // Alice's recipe is untouched
        var stillThere = await alice.GetFromJsonAsync<RecipeDetailResponse>(url);
        Assert.Equal("Alice's Secret Stew", stillThere!.Title);
    }

    [Fact]
    public async Task Users_cannot_add_others_recipes_to_their_meal_plan()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync("alice@test.local");
        var bob = await factory.CreateUserClientAsync("bob@test.local");

        var alicesRecipe = await (await alice.PostAsJsonAsync("/api/recipes", Sample()))
            .Content.ReadFromJsonAsync<RecipeResponse>();

        var monday = new DateOnly(2026, 10, 5);
        var bobsPlan = await (await bob.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Bob's week", monday)))
            .Content.ReadFromJsonAsync<MealPlanResponse>();

        // Indirect IDOR: Bob references Alice's recipe ID in his own plan
        var response = await bob.PutAsJsonAsync($"/api/mealplans/{bobsPlan!.Id}/entries",
            new[] { new MealPlanEntryRequest(monday, alicesRecipe!.Id, 4) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Short_passwords_are_rejected()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "weak@test.local", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_five_failed_logins()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        const string email = "target@test.local";

        await client.PostAsJsonAsync("/api/auth/register", new { email, password = TestAuth.Password });

        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login?useCookies=true",
                new { email, password = "wrong-password-guess" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // Even the correct password fails while locked out
        var correct = await client.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/recipes")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/recipes")).StatusCode);
    }
}