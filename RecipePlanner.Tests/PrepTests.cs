using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class PrepTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static async Task<(HttpClient Client, int PlanId)> PlanWeek(ApiFactory factory, params (int DayOffset, string Slug, int Servings)[] meals)
    {
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        var client = await factory.CreateUserClientAsync();
        var picks = meals.Select(m => new PlanPick(Monday.AddDays(m.DayOffset), $"lib:{m.Slug}", m.Servings)).ToList();
        var plan = await (await client.PostAsJsonAsync("/api/mealplans/plan", new CreatePlanRequest("Prep week", Monday, picks)))
            .Content.ReadFromJsonAsync<MealPlanResponse>();
        return (client, plan!.Id);
    }

    [Fact]
    public async Task Advises_cook_ahead_cook_fresh_or_freeze()
    {
        using var factory = new ApiFactory();
        var (client, id) = await PlanWeek(factory,
            (0, "aip-beef-stew", 6),                 // a stew: cook ahead
            (1, "aip-ginger-garlic-shrimp", 4),      // a stir-fry: prep ahead, cook fresh
            (5, "aip-chicken-vegetable-soup", 6));   // 6 days after Sunday prep: freeze

        var prep = await client.GetFromJsonAsync<PrepSessionResponse>($"/api/mealplans/{id}/prep");
        Assert.Equal(Monday.AddDays(-1), prep!.PrepDay); // defaults to the day before the week
        Assert.Equal(["CookAhead", "PrepAheadCookFresh", "Freeze"], prep.Meals.Select(m => m.Approach));
        Assert.Contains("3 to 4 days", prep.Meals[2].Reason);

        // The stew's long cook goes first; the stir-fry isn't cooked today
        Assert.Equal("AIP Hearty Beef Stew", prep.StartFirst.First().Title);
        Assert.DoesNotContain(prep.StartFirst, l => l.Title.Contains("Shrimp"));
    }

    [Fact]
    public async Task Combines_the_same_knife_work_across_recipes_and_scales_it()
    {
        using var factory = new ApiFactory();
        // AIP Chicken Adobo: 8 cloves garlic, smashed (serves 4). Planned twice at 8 servings each: 32 cloves
        var (client, id) = await PlanWeek(factory, (0, "aip-chicken-adobo", 8), (2, "aip-chicken-adobo", 8), (1, "aip-chicken-lettuce-wraps", 4));

        var prep = await client.GetFromJsonAsync<PrepSessionResponse>($"/api/mealplans/{id}/prep");
        var smashed = prep!.ChopOnce.Single(c => c.Ingredient == "Garlic" && c.Prep == "smashed");
        Assert.Equal(32m, smashed.Quantity);
        Assert.Equal(["AIP Chicken Adobo"], smashed.ForRecipes);
        var minced = prep.ChopOnce.Single(c => c.Ingredient == "Garlic" && c.Prep == "minced");
        Assert.Equal(["AIP Chicken Lettuce Wraps"], minced.ForRecipes);
        // Listed in the order you walk the store
        var ranks = prep.ChopOnce.Select(c => RecipePlanner.Api.Library.StoreSections.Rank(c.StoreSection)).ToList();
        Assert.Equal(ranks.Order(), ranks);
    }

    [Fact]
    public void Store_order_puts_produce_before_meat_and_unknown_last()
    {
        Assert.True(RecipePlanner.Api.Library.StoreSections.Rank("Produce") < RecipePlanner.Api.Library.StoreSections.Rank("Meat"));
        Assert.Equal(RecipePlanner.Api.Library.StoreSections.Order.Length, RecipePlanner.Api.Library.StoreSections.Rank(null));
        Assert.Equal(RecipePlanner.Api.Library.StoreSections.Order.Length, RecipePlanner.Api.Library.StoreSections.Rank("Garden"));
    }

    [Fact]
    public async Task Lists_recipes_that_need_marinating()
    {
        using var factory = new ApiFactory();
        var (client, id) = await PlanWeek(factory, (0, "aip-lemongrass-chicken", 4), (1, "aip-guacamole", 4));
        var prep = await client.GetFromJsonAsync<PrepSessionResponse>($"/api/mealplans/{id}/prep");
        Assert.Equal(["AIP Vietnamese-Style Lemongrass Chicken"], prep!.Marinate);
    }

    [Fact]
    public async Task Prep_day_can_be_chosen_and_plans_stay_private()
    {
        using var factory = new ApiFactory();
        var (client, id) = await PlanWeek(factory, (3, "aip-beef-stew", 4));

        var midweek = await client.GetFromJsonAsync<PrepSessionResponse>($"/api/mealplans/{id}/prep?prepDay={Monday.AddDays(2):yyyy-MM-dd}");
        Assert.Equal("CookAhead", midweek!.Meals.Single().Approach);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/mealplans/{id}/prep?prepDay=2020-01-01")).StatusCode);

        var stranger = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/mealplans/{id}/prep")).StatusCode);
    }
}
