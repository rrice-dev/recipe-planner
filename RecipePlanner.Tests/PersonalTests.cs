using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class PersonalTests
{
    private static async Task<int> NewRecipe(HttpClient client, string title = "Soup") =>
        (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, null, "Thai", 4, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;

    private static Task<RecipeDetailResponse?> Detail(HttpClient client, int id) =>
        client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{id}");

    [Fact]
    public async Task Favorites_and_notes_are_saved_and_shown_only_to_the_owner()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/recipes/{id}/favorite", new FavoriteRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/recipes/{id}/notes", new NotesRequest("  Use less salt next time  "))).StatusCode);

        var detail = await Detail(client, id);
        Assert.True(detail!.Personal!.IsFavorite);
        Assert.Equal("Use less salt next time", detail.Personal.Notes);
        Assert.True((await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes"))!.Single().IsFavorite);

        // Share the recipe: the public view must not reveal favorites or notes
        var token = (await (await client.PostAsync($"/api/recipes/{id}/share", null)).Content.ReadFromJsonAsync<Dictionary<string, string>>())!["shareToken"];
        var shared = await factory.CreateClient().GetStringAsync($"/api/shared/{token}");
        Assert.DoesNotContain("less salt", shared);
        Assert.DoesNotContain("isFavorite", shared, StringComparison.OrdinalIgnoreCase);

        // Clearing notes
        await client.PutAsJsonAsync($"/api/recipes/{id}/notes", new NotesRequest("   "));
        Assert.Null((await Detail(client, id))!.Personal!.Notes);
    }

    [Fact]
    public async Task Rejects_overlong_notes_and_other_peoples_recipes()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var id = await NewRecipe(owner);
        var stranger = await factory.CreateUserClientAsync();

        var tooLong = await owner.PutAsJsonAsync($"/api/recipes/{id}/notes", new NotesRequest(new string('x', 4001)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/recipes/{id}/favorite", new FavoriteRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync($"/api/recipes/{id}/notes", new NotesRequest("hi"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/recipes/{id}/cooked", new CookedRequest(null))).StatusCode);
        Assert.False((await Detail(owner, id))!.Personal!.IsFavorite);
    }

    [Fact]
    public async Task Cooking_log_counts_and_can_be_corrected()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        var first = await (await client.PostAsJsonAsync($"/api/recipes/{id}/cooked", new CookedRequest(new DateOnly(2026, 10, 1))))
            .Content.ReadFromJsonAsync<CookLogResponse>();
        await client.PostAsJsonAsync($"/api/recipes/{id}/cooked", new CookedRequest(new DateOnly(2026, 10, 12)));
        await client.PostAsync($"/api/recipes/{id}/cooked", null); // today

        var summary = (await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes"))!.Single();
        Assert.Equal(3, summary.TimesCooked);
        Assert.True(summary.LastCooked >= new DateOnly(2026, 10, 12));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/recipes/{id}/cooked/{first!.Id}")).StatusCode);
        var log = (await Detail(client, id))!.Personal!.Cooked;
        Assert.Equal(2, log.Count);
        Assert.True(log[0].CookedOn >= log[1].CookedOn); // newest first

        var future = await client.PostAsJsonAsync($"/api/recipes/{id}/cooked", new CookedRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)));
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        // Another user can't delete your entries, even knowing the IDs
        var stranger = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/recipes/{id}/cooked/{log[0].Id}")).StatusCode);
    }

    [Fact]
    public async Task Collections_group_recipes_without_owning_them()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var soup = await NewRecipe(client, "Soup");
        var stew = await NewRecipe(client, "Stew");

        var weeknight = await (await client.PostAsJsonAsync("/api/collections", new CollectionRequest(" Weeknight "))).Content.ReadFromJsonAsync<CollectionResponse>();
        var batch = await (await client.PostAsJsonAsync("/api/collections", new CollectionRequest("Batch cook"))).Content.ReadFromJsonAsync<CollectionResponse>();
        Assert.Equal("Weeknight", weeknight!.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/collections", new CollectionRequest("weeknight"))).StatusCode);

        await client.PutAsJsonAsync($"/api/recipes/{soup}/collections", new[] { weeknight.Id, batch!.Id });
        await client.PutAsJsonAsync($"/api/recipes/{stew}/collections", new[] { batch.Id });

        var list = await client.GetFromJsonAsync<List<CollectionResponse>>("/api/collections");
        Assert.Equal([("Batch cook", 2), ("Weeknight", 1)], list!.Select(c => (c.Name, c.RecipeCount)));
        Assert.Equal(new[] { batch.Id, weeknight.Id }.Order(), (await Detail(client, soup))!.Personal!.CollectionIds.Order());

        // Renaming, then deleting a collection keeps its recipes
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/collections/{batch.Id}", new CollectionRequest("Meal prep"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/collections/{batch.Id}")).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes"))!.Count);
        Assert.Equal([weeknight.Id], (await Detail(client, soup))!.Personal!.CollectionIds);

        // Someone else's collection can't be used or seen
        var stranger = await factory.CreateUserClientAsync();
        Assert.Empty((await stranger.GetFromJsonAsync<List<CollectionResponse>>("/api/collections"))!);
        var strangerRecipe = await NewRecipe(stranger);
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.PutAsJsonAsync($"/api/recipes/{strangerRecipe}/collections", new[] { weeknight.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/collections/{weeknight.Id}")).StatusCode);
    }

    [Fact]
    public async Task Recipe_list_includes_ingredients_and_diet_tags_for_search()
    {
        using var factory = new ApiFactory();
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        var client = await factory.CreateUserClientAsync();
        await client.PostAsync("/api/library/aip-guacamole/add", null);
        await client.PostAsync("/api/library/beef-and-broccoli/add", null);

        var list = (await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes"))!;
        var guac = list.Single(r => r.Title == "AIP Guacamole");
        Assert.Contains("Avocado", guac.IngredientNames!);
        Assert.Equal(["AIP", "Vegetarian"], guac.DietTags);
        Assert.Empty(list.Single(r => r.Title == "Beef and Broccoli").DietTags!);
    }

    [Fact]
    public async Task Unit_preference_is_a_setting()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        Assert.Equal("us", (await client.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Units);
        await client.PutAsJsonAsync("/api/settings", new SettingsRequest("system", null, 4, "metric"));
        Assert.Equal("metric", (await client.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Units);
        // Leaving it out keeps the current choice (older app versions don't send it)
        await client.PutAsJsonAsync("/api/settings", new SettingsRequest("dark", null, 4));
        Assert.Equal("metric", (await client.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Units);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/settings", new SettingsRequest("dark", null, 4, "imperial-ish"))).StatusCode);
    }
}
