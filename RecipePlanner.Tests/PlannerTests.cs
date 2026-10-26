using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;

namespace RecipePlanner.Tests;

public class WeekPlannerLogicTests
{
    private static WeekPlanner.Option Lib(string slug, string cuisine, params string[] tags) => new(
        new PlanCandidate($"lib:{slug}", slug, cuisine, 30, [.. tags], true, null, slug, null),
        diet => tags.Contains(diet));

    private static WeekPlanner.Option Mine(int id, string cuisine, string? slug = null, params string[] tags) => new(
        new PlanCandidate($"mine:{id}", $"mine {id}", cuisine, 30, [.. tags], false, id, slug, null),
        diet => tags.Contains(diet));

    [Fact]
    public void Picks_the_requested_number_without_repeats()
    {
        var options = Enumerable.Range(1, 20).Select(i => Lib($"r{i}", $"C{i % 4}")).ToList();
        var (picks, matching) = WeekPlanner.Suggest(options, 7, null, null, null, [], new Random(1));
        Assert.Equal(7, picks.Count);
        Assert.Equal(7, picks.Select(p => p.Key).Distinct().Count());
        Assert.Equal(20, matching);
    }

    [Fact]
    public void Avoids_the_same_cuisine_two_days_running_when_it_can()
    {
        var options = Enumerable.Range(1, 6).Select(i => Lib($"t{i}", "Thai"))
            .Concat(Enumerable.Range(1, 6).Select(i => Lib($"i{i}", "Italian"))).ToList();
        for (var seed = 0; seed < 20; seed++)
        {
            var (picks, _) = WeekPlanner.Suggest(options, 7, null, null, null, [], new Random(seed));
            for (var i = 1; i < picks.Count; i++)
                Assert.NotEqual(picks[i - 1].Cuisine, picks[i].Cuisine);
        }
    }

    [Fact]
    public void Respects_diet_tag_cuisine_and_exclusions()
    {
        var options = new[]
        {
            Lib("aip-thai", "Thai-inspired", "AIP", "Asian"), Lib("thai", "Thai", "Asian"),
            Lib("aip-us", "American", "AIP"), Mine(1, "Chinese", null, "AIP", "Asian"),
        };
        Assert.Equal(["aip-thai", "aip-us", "mine 1"],
            WeekPlanner.Suggest(options, 7, "AIP", null, null, [], new Random(0)).Picks.Select(p => p.Title).Order());
        Assert.Equal(["aip-thai", "mine 1"],
            WeekPlanner.Suggest(options, 7, "AIP", "Asian", null, [], new Random(0)).Picks.Select(p => p.Title).Order());
        Assert.Equal(["aip-thai", "thai"],
            WeekPlanner.Suggest(options, 7, null, null, "Thai", [], new Random(0)).Picks.Select(p => p.Title).Order());
        Assert.Equal(["aip-us", "mine 1"],
            WeekPlanner.Suggest(options, 7, "AIP", null, null, ["lib:aip-thai"], new Random(0)).Picks.Select(p => p.Title).Order());
    }

    [Fact]
    public void Leaves_out_sides_desserts_and_breakfasts_unless_asked()
    {
        var options = new[] { Lib("dinner", "American"), Lib("cookies", "American", "Dessert"), Lib("hash", "American", "Breakfast") };
        Assert.Equal(["dinner"], WeekPlanner.Suggest(options, 7, null, null, null, [], new Random(0)).Picks.Select(p => p.Title));
        Assert.Equal(["hash"], WeekPlanner.Suggest(options, 7, null, "Breakfast", null, [], new Random(0)).Picks.Select(p => p.Title));
    }

    [Fact]
    public void My_copy_of_a_library_recipe_replaces_the_original()
    {
        var options = new[] { Lib("adobo", "Filipino"), Mine(5, "Filipino", "adobo") };
        var (picks, matching) = WeekPlanner.Suggest(options, 7, null, null, null, [], new Random(0));
        Assert.Equal("mine:5", Assert.Single(picks).Key);
        Assert.Equal(1, matching);
    }
}

public class PlannerEndpointTests
{
    private static async Task Seed(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
    }

    private static readonly DateOnly Monday = new(2026, 11, 2);

    [Fact]
    public async Task Suggests_a_week_using_my_diet_setting()
    {
        using var factory = new ApiFactory();
        await Seed(factory);
        var client = await factory.CreateUserClientAsync();
        await client.PutAsJsonAsync("/api/settings", new SettingsRequest("system", "AIP", 4));

        var result = await (await client.PostAsJsonAsync("/api/mealplans/suggest", new PlanSuggestRequest(7, null, null, null, "both", null)))
            .Content.ReadFromJsonAsync<PlanSuggestResponse>();
        Assert.Equal("AIP", result!.Diet);
        Assert.Equal(7, result.Suggestions.Count);
        Assert.All(result.Suggestions, s => Assert.Contains("AIP", s.Tags));
        Assert.DoesNotContain(result.Suggestions, s => s.Tags.Contains("Side"));

        // Turning the setting off for one request
        var any = await (await client.PostAsJsonAsync("/api/mealplans/suggest", new PlanSuggestRequest(7, null, null, null, "library", null, UseMyDiet: false)))
            .Content.ReadFromJsonAsync<PlanSuggestResponse>();
        Assert.Null(any!.Diet);
        Assert.True(any.Matching > result.Matching);
    }

    [Fact]
    public async Task My_own_recipes_are_checked_against_the_diet()
    {
        using var factory = new ApiFactory();
        await Seed(factory);
        var client = await factory.CreateUserClientAsync();
        // A library copy keeps its ingredients, so the AIP check can run on it
        await client.PostAsync("/api/library/aip-chicken-adobo/add", null);
        await client.PostAsync("/api/library/chicken-adobo/add", null); // has soy sauce and sugar: not AIP

        var result = await (await client.PostAsJsonAsync("/api/mealplans/suggest", new PlanSuggestRequest(14, "AIP", null, null, "mine", null)))
            .Content.ReadFromJsonAsync<PlanSuggestResponse>();
        var only = Assert.Single(result!.Suggestions);
        Assert.Equal("AIP Chicken Adobo", only.Title);
        Assert.StartsWith("mine:", only.Key);
    }

    [Fact]
    public async Task Saving_a_plan_copies_library_recipes_once_and_builds_the_shopping_list()
    {
        using var factory = new ApiFactory();
        await Seed(factory);
        var client = await factory.CreateUserClientAsync();
        var mine = await (await client.PostAsync("/api/library/beef-and-broccoli/add", null)).Content.ReadFromJsonAsync<LibraryAddResponse>();

        var picks = new List<PlanPick>
        {
            new(Monday, "lib:beef-and-broccoli", 4),            // already copied: reused, not copied again
            new(Monday.AddDays(1), "lib:chicken-teriyaki", 2),  // these two share ingredients that may not exist yet
            new(Monday.AddDays(2), "lib:aip-chicken-teriyaki", 4),
            new(Monday.AddDays(3), "lib:chicken-teriyaki", 4),  // same recipe twice in a week: one copy
        };
        var response = await client.PostAsJsonAsync("/api/mealplans/plan", new CreatePlanRequest("AIP week", Monday, picks));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<MealPlanResponse>();
        Assert.Equal(4, plan!.Entries.Count);
        Assert.Equal(mine!.RecipeId, plan.Entries[0].RecipeId);
        Assert.Equal(plan.Entries[1].RecipeId, plan.Entries[3].RecipeId);

        var recipes = await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes");
        Assert.Equal(3, recipes!.Count); // beef and broccoli, teriyaki, AIP teriyaki

        // Saving again reuses all three copies
        await client.PostAsJsonAsync("/api/mealplans/plan", new CreatePlanRequest("Again", Monday, picks));
        Assert.Equal(3, (await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes"))!.Count);

        var list = await client.GetFromJsonAsync<ShoppingListResponse>($"/api/mealplans/{plan.Id}/shopping-list");
        Assert.Contains(list!.Items, i => i.Ingredient == "Flank Steak");
    }

    [Fact]
    public async Task Rejects_other_peoples_recipes_and_bad_input()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var theirs = await (await owner.PostAsJsonAsync("/api/recipes", new RecipeRequest("Secret", null, null, 4, 1, 1)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var client = await factory.CreateUserClientAsync();

        async Task<HttpStatusCode> Save(params PlanPick[] p) =>
            (await client.PostAsJsonAsync("/api/mealplans/plan", new CreatePlanRequest("Week", Monday, [.. p]))).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Save(new PlanPick(Monday, $"mine:{theirs!.Id}", 4)));
        Assert.Equal(HttpStatusCode.BadRequest, await Save(new PlanPick(Monday, "lib:not-a-recipe", 4)));
        Assert.Equal(HttpStatusCode.BadRequest, await Save(new PlanPick(Monday.AddDays(7), "lib:chicken-adobo", 4)));
        Assert.Equal(HttpStatusCode.BadRequest, await Save(new PlanPick(Monday, "lib:chicken-adobo", 0)));
        Assert.Equal(HttpStatusCode.BadRequest, await Save());

        var tooMany = await client.PostAsJsonAsync("/api/mealplans/suggest", new PlanSuggestRequest(30, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }
}
