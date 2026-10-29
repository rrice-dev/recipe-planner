using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Endpoints;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Tests;

// Fixes from the third review pass
public class EmailAllowanceTests
{
    private static int SentTo(FakeEmail mail, string to, string subjectPart) =>
        mail.Sent.Count(m => m.To == to && m.Subject.Contains(subjectPart));

    [Fact]
    public async Task A_second_reset_email_within_15_minutes_is_not_sent()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"reset-{Guid.NewGuid():N}@test.local";
        await factory.CreateUserClientAsync(email);
        var anon = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/account/forgot-password", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/account/forgot-password", new { email })).StatusCode);

        // Same answer both times (nothing to learn from it), but only one email
        Assert.Equal(1, SentTo(mail, email, "Reset"));
    }

    [Fact]
    public async Task Resending_a_confirmation_right_after_sign_up_waits_out_the_cooldown()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"confirm-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);
        Assert.Equal(1, SentTo(mail, email, "Confirm"));   // the sign-up email

        await client.PostAsJsonAsync("/api/auth/resendConfirmationEmail", new { email });
        Assert.Equal(1, SentTo(mail, email, "Confirm"));

        // 16 minutes later it goes out
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EmailCooldowns.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSentAt, c => c.LastSentAt.AddMinutes(-16)));
        }
        await client.PostAsJsonAsync("/api/auth/resendConfirmationEmail", new { email });
        Assert.Equal(2, SentTo(mail, email, "Confirm"));
    }

    [Fact]
    public async Task A_reset_email_is_not_held_up_by_the_sign_up_email()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var email = $"both-{Guid.NewGuid():N}@test.local";
        await factory.CreateUserClientAsync(email);

        await factory.CreateClient().PostAsJsonAsync("/api/account/forgot-password", new { email });
        Assert.Equal(1, SentTo(mail, email, "Reset"));
    }

    [Fact]
    public async Task One_ip_can_make_only_five_email_sending_calls_an_hour()
    {
        var mail = new FakeEmail();
        using var factory = new ApiFactory { Email = mail };
        var anon = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await anon.PostAsJsonAsync("/api/account/forgot-password", new { email = $"nobody{i}@test.local" })).StatusCode);

        foreach (var (path, body) in new (string, object)[]
                 {
                     ("/api/account/forgot-password", new { email = "nobody@test.local" }),
                     ("/api/auth/forgotPassword", new { email = "nobody@test.local" }),
                     ("/api/auth/resendConfirmationEmail", new { email = "nobody@test.local" }),
                     ("/api/auth/register", new { email = "new@test.local", password = TestAuth.Password })
                 })
            Assert.Equal(HttpStatusCode.TooManyRequests, (await anon.PostAsJsonAsync(path, body)).StatusCode);
    }
}

public class RecipeNumberLimitTests
{
    private static object Recipe(int servings = 4, int prep = 10, int cook = 20) =>
        new { title = "Chili", servings, prepMinutes = prep, cookMinutes = cook };

    [Fact]
    public async Task Servings_are_capped_at_100_like_everywhere_else()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/recipes", Recipe(servings: 100))).StatusCode);
        var tooMany = await client.PostAsJsonAsync("/api/recipes", Recipe(servings: 101));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("Servings", (await tooMany.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task Prep_and_cook_times_are_capped_at_a_week()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/recipes", Recipe(prep: 10_080, cook: 10_080))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes", Recipe(prep: 10_081))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes", Recipe(cook: int.MaxValue))).StatusCode);
    }
}

public class MealPlanWeekRangeTests
{
    [Fact]
    public async Task A_week_far_in_the_future_is_refused_instead_of_crashing_later()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/mealplans", new { name = "Far", weekStart = "9999-12-31" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/mealplans", new { name = "Long ago", weekStart = "0001-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/mealplans/plan", new
        {
            name = "Far", weekStart = "9999-12-31",
            picks = new[] { new { day = "9999-12-31", key = "lib:beef-bulgogi", servings = 2 } }
        })).StatusCode);
    }

    [Fact]
    public async Task Weeks_within_a_few_years_still_work()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var start in new[] { today.AddYears(-4), today, today.AddYears(4) })
            Assert.Equal(HttpStatusCode.Created,
                (await client.PostAsJsonAsync("/api/mealplans", new { name = "Ok", weekStart = start })).StatusCode);
    }
}

public class AccountCapTests
{
    private static async Task<string> UserId(ApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email))!.Id;
    }

    private static async Task Seed(ApiFactory factory, Action<AppDbContext> add)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        add(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Recipes_stop_at_the_cap_however_they_are_added()
    {
        using var factory = new ApiFactory();
        var email = $"cap-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);
        var userId = await UserId(factory, email);
        await Seed(factory, db => db.Recipes.AddRange(Enumerable.Range(0, AccountLimits.MaxRecipes)
            .Select(i => new Recipe { Title = $"R{i}", Servings = 1, OwnerId = userId })));

        var typed = await client.PostAsJsonAsync("/api/recipes", new { title = "One more", servings = 2, prepMinutes = 0, cookMinutes = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, typed.StatusCode);
        Assert.Contains("Recipes", (await typed.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/library/beef-bulgogi/add", null)).StatusCode);

        var monday = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/mealplans/plan", new
        {
            name = "Week", weekStart = monday,
            picks = new[] { new { day = monday, key = "lib:beef-bulgogi", servings = 2 } }
        })).StatusCode);
    }

    [Fact]
    public async Task Meal_plans_stop_at_the_cap()
    {
        using var factory = new ApiFactory();
        var email = $"plans-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);
        var userId = await UserId(factory, email);
        var monday = DateOnly.FromDateTime(DateTime.UtcNow);
        await Seed(factory, db => db.MealPlans.AddRange(Enumerable.Range(0, AccountLimits.MaxMealPlans)
            .Select(i => new MealPlan { Name = $"P{i}", WeekStart = monday, OwnerId = userId })));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/mealplans", new { name = "One more", weekStart = monday })).StatusCode);
    }

    [Fact]
    public async Task Cooked_it_entries_stop_at_the_cap_per_recipe()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var created = await client.PostAsJsonAsync("/api/recipes", new { title = "Daily oats", servings = 1, prepMinutes = 0, cookMinutes = 5 });
        var id = (await created.Content.ReadFromJsonAsync<RecipePlanner.Api.Contracts.RecipeResponse>())!.Id;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Seed(factory, db => db.CookLog.AddRange(Enumerable.Range(0, AccountLimits.MaxCookLogEntries)
            .Select(i => new CookLogEntry { RecipeId = id, CookedOn = today.AddDays(-i % 3000) })));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/recipes/{id}/cooked", new { })).StatusCode);
    }
}

public class AiDailyLimitMessageTests
{
    [Fact]
    public async Task Pages_say_ai_is_off_for_the_day_rather_than_for_a_minute()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Anthropic:ApiKey", "test-key-not-real");
            b.UseSetting("AiLimits:DailyCalls", "0");
        });
        var signedIn = factory.CreateClient();
        var email = $"ai-{Guid.NewGuid():N}@test.local";
        (await signedIn.PostAsJsonAsync("/api/auth/register", new { email, password = TestAuth.Password })).EnsureSuccessStatusCode();
        (await signedIn.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestAuth.Password })).EnsureSuccessStatusCode();

        var suggestions = await signedIn.PostAsJsonAsync("/api/fridge/suggestions", new { ingredients = new[] { "eggs" } });
        Assert.Equal(HttpStatusCode.OK, suggestions.StatusCode);
        Assert.Contains(AiErrors.DailyLimitMessage, await suggestions.Content.ReadAsStringAsync());

        var recipe = await signedIn.PostAsJsonAsync("/api/world/recipe", new { country = "Peru", dishName = "Lomo saltado" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, recipe.StatusCode);
        Assert.Contains(AiErrors.DailyLimitMessage, await recipe.Content.ReadAsStringAsync());

        var text = await signedIn.PostAsJsonAsync("/api/import/text", new { text = "Pancakes: 2 eggs, 1 cup flour. Whisk and fry in butter." });
        Assert.Contains(AiErrors.DailyLimitMessage, await text.Content.ReadAsStringAsync());
    }
}
