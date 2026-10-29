using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Email;
using RecipePlanner.Api.Nutrition;
using System.Threading.Channels;

namespace RecipePlanner.Tests;

public class FakeEmail : IEmailTransport
{
    public const string SiteUrl = "https://recipes.example.com";
    public ConcurrentQueue<(string To, string Subject, string Html, string Text)> Sent { get; } = new();
    public Task SendAsync(string to, string subject, string html, string text) { Sent.Enqueue((to, subject, html, text)); return Task.CompletedTask; }
}

public class PasswordResetTests
{
    [Fact]
    public async Task Features_report_what_is_set_up()
    {
        using (var off = new ApiFactory())
            Assert.Equal(new FeaturesResponse(false, false), await off.CreateClient().GetFromJsonAsync<FeaturesResponse>("/api/features"));
        using var on = new ApiFactory { Email = new FakeEmail(), Foods = new FakeFoods() };
        Assert.Equal(new FeaturesResponse(true, true), await on.CreateClient().GetFromJsonAsync<FeaturesResponse>("/api/features"));
    }

    [Fact]
    public async Task Reset_link_is_emailed_and_works_once()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"forgetful-{Guid.NewGuid():N}@test.local";
        await factory.CreateUserClientAsync(email);
        var anon = factory.CreateClient();

        // An attacker-controlled Host must not end up in the link (host header injection)
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/forgot-password") { Content = JsonContent.Create(new { email }) };
        request.Headers.Host = "evil.example";
        request.Headers.Add("X-Forwarded-Host", "evil.example");
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(request)).StatusCode);

        // Sign-up also sent a confirmation email; this is about the reset one
        var sent = Assert.Single(mail.Sent, m => m.Subject.Contains("Reset"));
        Assert.Equal(email, sent.To);
        var link = Regex.Match(sent.Text, @"https://\S+").Value;
        Assert.StartsWith(FakeEmail.SiteUrl + "/reset-password?", link);
        Assert.DoesNotContain("evil", sent.Html + sent.Text);

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
        Assert.Equal(email, query["email"]);
        var newPassword = "a whole new kitchen password";
        var reset = await anon.PostAsJsonAsync("/api/account/reset-password", new { email, code = query["code"], newPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = newPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestAuth.Password })).StatusCode);

        // The same code can't be used twice
        var again = await anon.PostAsJsonAsync("/api/account/reset-password", new { email, code = query["code"], newPassword = "yet another long password" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Bad_codes_and_weak_passwords_are_refused()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"r-{Guid.NewGuid():N}@test.local";
        await factory.CreateUserClientAsync(email);
        var anon = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/account/reset-password", new { email, code = "not-a-real-code", newPassword = "long enough password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/account/reset-password", new { email = "nobody@test.local", code = "x", newPassword = "long enough password" })).StatusCode);

        await anon.PostAsJsonAsync("/api/account/forgot-password", new { email });
        var code = System.Web.HttpUtility.ParseQueryString(new Uri(Regex.Match(mail.Sent.Single(m => m.Subject.Contains("Reset")).Text, @"https://\S+").Value).Query)["code"];
        var weak = await anon.PostAsJsonAsync("/api/account/reset-password", new { email, code, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("12", await weak.Content.ReadAsStringAsync()); // explains the length rule
    }

    [Fact]
    public async Task Reset_is_off_until_email_is_set_up()
    {
        using var factory = new ApiFactory();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().PostAsJsonAsync("/api/account/forgot-password", new { email = "a@b.c" })).StatusCode);
    }

    [Fact]
    public async Task Unknown_emails_get_the_same_answer_and_no_email()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var response = await factory.CreateClient().PostAsJsonAsync("/api/account/forgot-password", new { email = "nobody@test.local" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // can't be used to find out who has an account
        Assert.Empty(mail.Sent);
    }
}

public class FakeFoods : IFoodSource
{
    public ConcurrentDictionary<string, int> Lookups { get; } = new();
    public bool IsAvailable => true;

    public static readonly Dictionary<string, FoodFacts> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chicken thighs"] = new(1, "Chicken, thigh, meat only, raw", new(121, 19.7, 0, 4.1, 0, 0, 95), []),
        ["olive oil"] = new(2, "Oil, olive", new(884, 0, 0, 100, 0, 0, 2), [new("tbsp", 1, 13.5), new("cup", 1, 216)]),
        ["yellow onion"] = new(3, "Onions, raw", new(40, 1.1, 9.3, 0.1, 1.7, 4.2, 4), [new("cup, chopped", 1, 160), new("medium (2-1/2\" dia)", 1, 110)]),
    };

    public Task<FoodFacts?> FindAsync(string ingredient, CancellationToken ct)
    {
        Lookups.AddOrUpdate(ingredient, 1, (_, n) => n + 1);
        return Task.FromResult(Known.GetValueOrDefault(ingredient));
    }
}

public class NutritionTests
{
    [Fact]
    public void Converts_amounts_to_grams_using_usda_portions()
    {
        var onion = FakeFoods.Known["yellow onion"];
        Assert.Equal((320d, false), NutritionCalculator.ToGrams(2, "cup", onion));
        Assert.Equal((110d, false), NutritionCalculator.ToGrams(1, "whole", onion));
        Assert.Equal((453.592, false), NutritionCalculator.ToGrams(1, "lb", onion));
        var tbspOil = NutritionCalculator.ToGrams(3, "tsp", FakeFoods.Known["olive oil"])!.Value;
        Assert.Equal(13.5, tbspOil.Grams, 0.2); // 3 tsp = 1 tbsp, via the cup portion
        Assert.True(tbspOil.Approximate);
        Assert.Null(NutritionCalculator.ToGrams(1, "clove", onion)); // no weight for that: not counted, not guessed
    }

    [Fact]
    public void Reads_usda_food_details()
    {
        var json = """
        {"fdcId":170000,"description":"Onions, raw","foodNutrients":[
          {"nutrient":{"number":"208","name":"Energy","unitName":"kcal"},"amount":40},
          {"nutrient":{"number":"268","name":"Energy","unitName":"kJ"},"amount":166},
          {"nutrient":{"number":"203","name":"Protein"},"amount":1.1},
          {"nutrient":{"number":"205","name":"Carbohydrate"},"amount":9.34},
          {"nutrient":{"number":"307","name":"Sodium"},"amount":4}],
         "foodPortions":[
          {"amount":1,"gramWeight":160,"modifier":"cup, chopped","measureUnit":{"name":"undetermined"}},
          {"amount":1,"gramWeight":110,"measureUnit":{"name":"medium"}},
          {"amount":0,"gramWeight":0,"modifier":"bad"}]}
        """;
        var food = FoodDataCentralClient.Parse(JsonDocument.Parse(json).RootElement)!;
        Assert.Equal(170000, food.FdcId);
        Assert.Equal(40, food.Per100g.Calories);
        Assert.Equal(9.34, food.Per100g.Carbs);
        Assert.Equal(["cup, chopped", "medium"], food.Portions.Select(p => p.Unit));
    }

    private static async Task<(ApiFactory, HttpClient, int)> RecipeWith(FakeFoods foods)
    {
        var factory = new ApiFactory { Foods = foods };
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        var client = await factory.CreateUserClientAsync();
        var id = (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Chicken and onions", null, null, 2, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;
        var all = await client.GetFromJsonAsync<List<IngredientResponse>>("/api/ingredients");
        int Ing(string name) => all!.Single(i => i.Name == name).Id;
        await client.PutAsJsonAsync($"/api/recipes/{id}/ingredients", new[]
        {
            new RecipeIngredientRequest(Ing("Chicken Thighs"), 1, "lb", null),       // 453.6 g x 121 kcal/100 g = 549
            new RecipeIngredientRequest(Ing("Olive Oil"), 2, "tbsp", null),          // 27 g x 884 = 239
            new RecipeIngredientRequest(Ing("Yellow Onion"), 1, "whole", null),      // 110 g x 40 = 44
            new RecipeIngredientRequest(Ing("Saffron"), 1, "pinch", null),           // no USDA match: not counted
        });
        return (factory, client, id);
    }

    [Fact]
    public async Task Calculates_per_serving_and_explains_what_was_counted()
    {
        var foods = new FakeFoods();
        var (factory, client, id) = await RecipeWith(foods);
        using var _ = factory;

        var n = await client.GetFromJsonAsync<NutritionResponse>($"/api/recipes/{id}/nutrition");
        Assert.True(n!.Available);
        Assert.Equal((3, 4), (n.Counted, n.Total));
        Assert.InRange(n.PerServing!.Calories, 410, 420);  // (549 + 239 + 44) / 2 servings
        Assert.Equal(22.9, n.PerServing.Fat, 0.2);        // (18.6 thighs + 27 oil + 0.1 onion) / 2 servings
        Assert.Contains(n.Lines, l => l.Ingredient == "Saffron" && !l.Counted);
        Assert.Contains(n.Lines, l => l.Ingredient == "Yellow Onion" && l.MatchedFood == "Onions, raw" && l.Grams == 110);
    }

    [Fact]
    public async Task Each_ingredient_is_looked_up_once_even_across_users()
    {
        var foods = new FakeFoods();
        var (factory, client, id) = await RecipeWith(foods);
        using var _ = factory;
        await client.GetAsync($"/api/recipes/{id}/nutrition");
        await client.GetAsync($"/api/recipes/{id}/nutrition");
        await factory.CreateClient().GetAsync("/api/library/aip-chicken-adobo/nutrition"); // shares chicken thighs
        Assert.Equal(1, foods.Lookups["chicken thighs"]);
        Assert.Equal(1, foods.Lookups["saffron"]); // a miss is remembered too
    }

    [Fact]
    public async Task Without_a_key_nutrition_is_simply_unavailable()
    {
        using var factory = new ApiFactory();
        var n = await factory.CreateClient().GetFromJsonAsync<NutritionResponse>("/api/library/aip-guacamole/nutrition");
        Assert.False(n!.Available);
        Assert.Null(n.PerServing);
    }

    [Fact]
    public async Task Other_peoples_recipes_are_private()
    {
        var (factory, _, id) = await RecipeWith(new FakeFoods());
        using var __ = factory;
        var stranger = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/recipes/{id}/nutrition")).StatusCode);
    }
}

public class EmailQueueTests
{
    private sealed class FlakyDelivery : IEmailDelivery
    {
        public ConcurrentQueue<string> Delivered { get; } = new();
        public Task DeliverAsync(QueuedEmail email, CancellationToken ct)
        {
            if (email.Subject == "boom") throw new AggregateException("Retry failed after 3 tries.");
            Delivered.Enqueue(email.To);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_failed_email_never_stops_the_worker()
    {
        var queue = System.Threading.Channels.Channel.CreateUnbounded<QueuedEmail>();
        var delivery = new FlakyDelivery();
        var worker = new EmailSendingService(queue, delivery, Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailSendingService>.Instance);
        await worker.StartAsync(CancellationToken.None);

        var transport = new BackgroundEmailTransport(queue, Microsoft.Extensions.Logging.Abstractions.NullLogger<BackgroundEmailTransport>.Instance);
        var queued = transport.SendAsync("a@test.local", "boom", "", "");
        Assert.True(queued.IsCompletedSuccessfully); // the caller never waits on delivery
        await transport.SendAsync("b@test.local", "hello", "", "");

        for (var i = 0; i < 50 && delivery.Delivered.IsEmpty; i++) await Task.Delay(20);
        Assert.Equal(["b@test.local"], delivery.Delivered);
        Assert.False(worker.ExecuteTask!.IsCompleted); // still running after the failure
        await worker.StopAsync(CancellationToken.None);
    }
}
