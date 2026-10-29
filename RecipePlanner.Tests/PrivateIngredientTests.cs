using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Endpoints;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Tests;

// Ingredients people add are theirs alone; the built-in list (seeded, from the library, or added by an admin)
// is shared. Before this, anything one user typed showed up for every user and every logged-out visitor.
public class PrivateIngredientTests
{
    private static async Task<HttpResponseMessage> Create(HttpClient client, string name) =>
        await client.PostAsJsonAsync("/api/ingredients", new IngredientRequest(name, null));

    private static async Task<List<string>> Names(HttpClient client) =>
        (await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients"))!.Select(i => i.Name).ToList();

    [Fact]
    public async Task An_ingredient_a_user_adds_is_visible_only_to_them()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var created = await (await Create(alice, "Visit my spam site")).Content.ReadFromJsonAsync<IngredientResponse>();

        Assert.Contains("Visit my spam site", await Names(alice));
        Assert.DoesNotContain("Visit my spam site", await Names(bob));
        Assert.DoesNotContain("Visit my spam site", await Names(factory.CreateClient()));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/ingredients/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/ingredients/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Someone_elses_private_ingredient_cant_go_in_your_recipe()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var hers = await (await Create(alice, "Alice's spice")).Content.ReadFromJsonAsync<IngredientResponse>();
        var recipe = await (await bob.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", null, null, 2, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();

        var response = await bob.PutAsJsonAsync($"/api/recipes/{recipe!.Id}/ingredients",
            new[] { new RecipeIngredientRequest(hers!.Id, 1, "tsp", null) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_people_can_each_add_the_same_name()
    {
        using var factory = new ApiFactory();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await Create(alice, "Grandma's spice mix")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(bob, "Grandma's spice mix")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(bob, "Grandma's spice mix")).StatusCode); // but not twice
    }

    [Fact]
    public async Task Admin_added_ingredients_are_shared_and_users_cant_duplicate_them()
    {
        using var factory = new ApiFactory();
        var admin = await factory.CreateAdminClientAsync();
        var user = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await Create(admin, "Sumac")).StatusCode);
        Assert.Contains("Sumac", await Names(factory.CreateClient()));
        Assert.Contains("Sumac", await Names(user));
        Assert.Equal(HttpStatusCode.Conflict, (await Create(user, "Sumac")).StatusCode);
    }

    [Fact]
    public async Task There_is_a_cap_on_how_many_ingredients_one_person_adds()
    {
        using var factory = new ApiFactory();
        var email = $"collector-{Guid.NewGuid():N}@test.local";
        var user = await factory.CreateUserClientAsync(email);
        Assert.Equal(HttpStatusCode.Created, (await Create(user, "Thing 0")).StatusCode);

        // Fill up to the cap directly (through the API would take hundreds of calls and hit the rate limit)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = (await db.Ingredients.SingleAsync(i => i.Name == "Thing 0")).OwnerId;
            for (var i = 1; i < IngredientEndpoints.MaxPerUser; i++)
                db.Ingredients.Add(new Ingredient { Name = $"Thing {i}", OwnerId = owner });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Create(user, "One too many")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(await factory.CreateUserClientAsync(), "Someone else's")).StatusCode);
    }

    // A private "garlic" next to the shared "Garlic" used to crash adding a library recipe (duplicate key)
    [Fact]
    public async Task Adding_a_library_recipe_works_when_you_have_a_same_named_ingredient()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateUserClientAsync();
        var library = (await user.GetFromJsonAsync<List<LibrarySummaryResponse>>("/api/library"))!;
        var detail = (await user.GetFromJsonAsync<LibraryDetailResponse>($"/api/library/{library[0].Slug}"))!;
        var name = detail.Ingredients[0].Name;

        // Someone adds it to their book first, which creates the shared spelling
        var other = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsync($"/api/library/{library[0].Slug}/add", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(user, name.ToUpperInvariant())).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await user.PostAsync($"/api/library/{library[0].Slug}/add", null)).StatusCode);
    }
}
