using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class PlanNutritionTests
{
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static async Task<(ApiFactory Factory, HttpClient Client, int PlanId)> PlanWith(FakeFoods? foods)
    {
        var factory = new ApiFactory { Foods = foods };
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        var client = await factory.CreateUserClientAsync();
        var all = await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients");
        int Ing(string name) => all!.Single(i => i.Name == name).Id;

        async Task<int> Recipe(string title, int servings, params RecipeIngredientRequest[] lines)
        {
            var id = (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, null, null, servings, 5, 20)))
                .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;
            (await client.PutAsJsonAsync($"/api/recipes/{id}/ingredients", lines)).EnsureSuccessStatusCode();
            return id;
        }

        // 1 lb thighs (549 kcal, 18.6 g fat, 89.4 g protein) over 2 servings = 274 kcal each
        var chicken = await Recipe("Chicken", 2, new RecipeIngredientRequest(Ing("Chicken Thighs"), 1, "lb", null));
        // 2 tbsp oil (239 kcal) + saffron (no match) over 1 serving: counted, but incomplete
        var oily = await Recipe("Saffron oil", 1,
            new RecipeIngredientRequest(Ing("Olive Oil"), 2, "tbsp", null), new RecipeIngredientRequest(Ing("Saffron"), 1, "pinch", null));

        var plan = (await (await client.PostAsJsonAsync("/api/mealplans", new MealPlanRequest("Week", Monday)))
            .Content.ReadFromJsonAsync<MealPlanResponse>())!;
        (await client.PutAsJsonAsync($"/api/mealplans/{plan.Id}/entries", new[]
        {
            new MealPlanEntryRequest(Monday, chicken, 4),            // cooking 4 servings doesn't change one serving's numbers
            new MealPlanEntryRequest(Monday, oily, 1),
            new MealPlanEntryRequest(Monday.AddDays(2), chicken, 2),
        })).EnsureSuccessStatusCode();
        return (factory, client, plan.Id);
    }

    [Fact]
    public async Task Each_day_totals_one_serving_of_each_meal()
    {
        var (factory, client, planId) = await PlanWith(new FakeFoods());
        using var _ = factory;

        var n = (await client.GetFromJsonAsync<PlanNutritionResponse>($"/api/mealplans/{planId}/nutrition"))!;

        Assert.True(n.Available);
        Assert.Equal([Monday, Monday.AddDays(2)], n.Days.Select(d => d.Day));
        var monday = n.Days[0];
        Assert.Equal(["Chicken", "Saffron oil"], monday.Meals.Select(m => m.Title));
        Assert.InRange(monday.Meals[0].PerServing!.Calories, 272, 276);
        Assert.InRange(monday.Total.Calories, 511, 515);                 // 274 + 239
        Assert.InRange(n.Days[1].Total.Calories, 272, 276);
        Assert.InRange(n.DailyAverage!.Calories, 392, 395);              // (513 + 274) / 2 days with meals
        Assert.False(monday.Meals[1].Complete);                          // saffron couldn't be counted
        Assert.False(n.Complete);
    }

    [Fact]
    public async Task Targets_are_saved_validated_and_returned_with_the_plan()
    {
        var (factory, client, planId) = await PlanWith(new FakeFoods());
        using var _ = factory;

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/settings/nutrition-targets", new NutritionTargets(100, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/settings/nutrition-targets", new NutritionTargets(null, -5, null, null))).StatusCode);

        var saved = await (await client.PutAsJsonAsync("/api/settings/nutrition-targets", new NutritionTargets(2200, 150, null, 70)))
            .Content.ReadFromJsonAsync<NutritionTargets>();
        Assert.Equal(new NutritionTargets(2200, 150, null, 70), saved);
        Assert.Equal(saved, await client.GetFromJsonAsync<NutritionTargets>("/api/settings/nutrition-targets"));

        var n = (await client.GetFromJsonAsync<PlanNutritionResponse>($"/api/mealplans/{planId}/nutrition"))!;
        Assert.Equal(saved, n.Targets);
    }

    [Fact]
    public async Task Says_unavailable_when_nutrition_is_not_set_up()
    {
        var (factory, client, planId) = await PlanWith(null);
        using var _ = factory;
        var n = (await client.GetFromJsonAsync<PlanNutritionResponse>($"/api/mealplans/{planId}/nutrition"))!;
        Assert.False(n.Available);
        Assert.Empty(n.Days);
    }

    [Fact]
    public async Task Only_the_owner_can_see_a_plans_nutrition()
    {
        var (factory, _, planId) = await PlanWith(new FakeFoods());
        using var __ = factory;
        var other = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/mealplans/{planId}/nutrition")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/mealplans/{planId}/nutrition")).StatusCode);
    }
}
