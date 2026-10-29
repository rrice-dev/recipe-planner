using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class AiDailyLimitTests
{
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (AiDailyLimit Limit, ServiceProvider Services) Create(int dailyCalls, TimeProvider clock)
    {
        var connection = TestDatabase.ConnectionString(Guid.NewGuid().ToString());
        var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseNpgsql(connection))
            .BuildServiceProvider();
        var limit = new AiDailyLimit(services.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new AiDailyLimitOptions { DailyCalls = dailyCalls }), NullLogger<AiDailyLimit>.Instance);
        return (limit, services);
    }

    [Fact]
    public async Task Allows_calls_up_to_the_limit_then_refuses()
    {
        var (limit, services) = Create(3, new FakeClock(new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero)));
        using var _ = services;

        for (var i = 0; i < 3; i++) Assert.True(await limit.TryUseAsync(CancellationToken.None));
        Assert.False(await limit.TryUseAsync(CancellationToken.None));

        // A refused call isn't recorded, so it doesn't push tomorrow's count around
        using var scope = services.CreateScope();
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiUsage.CountAsync());
    }

    [Fact]
    public async Task The_limit_resets_at_midnight_utc()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 29, 23, 59, 0, TimeSpan.Zero));
        var (limit, services) = Create(1, clock);
        using var _ = services;

        Assert.True(await limit.TryUseAsync(CancellationToken.None));
        Assert.False(await limit.TryUseAsync(CancellationToken.None));

        clock.Now = new DateTimeOffset(2026, 10, 30, 0, 0, 1, TimeSpan.Zero);
        Assert.True(await limit.TryUseAsync(CancellationToken.None));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Handler_stops_the_request_from_leaving_once_the_limit_is_used()
    {
        var (limit, services) = Create(1, new FakeClock(DateTimeOffset.UtcNow));
        using var _ = services;
        var outside = new CountingHandler();
        using var http = new HttpClient(new AiDailyLimitHandler(limit) { InnerHandler = outside });

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("https://api.example.test/")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.GetAsync("https://api.example.test/")).StatusCode);
        Assert.Equal(1, outside.Calls);
    }

    // With a key set and a limit of zero, every real AI class must stop at the limit with no network call.
    // If one of them were missing the handler, it would try to reach Anthropic and fail differently.
    [Fact]
    public async Task Every_ai_feature_goes_through_the_daily_limit()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Anthropic:ApiKey", "test-key-not-real");
            b.UseSetting("AiLimits:DailyCalls", "0");
        });
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ct = CancellationToken.None;
        var jpeg = TestImages.Jpeg(64, 64);

        var calls = new Func<Task>[]
        {
            () => sp.GetRequiredService<IMealIdeaGenerator>().GenerateAsync(["eggs"], ["Boiling"], ct),
            () => sp.GetRequiredService<IIngredientScanner>().ScanAsync(jpeg, ct),
            () => sp.GetRequiredService<IRecipeDraftGenerator>().GenerateAsync("Omelette", ["eggs"], ["Boiling"], ct),
            () => sp.GetRequiredService<ICountryDishGenerator>().GenerateAsync("Peru", [], ct),
            () => sp.GetRequiredService<IRecipeExtractor>().ExtractAsync("Pancakes. 2 eggs. Mix and fry.", ct),
            () => sp.GetRequiredService<IRecipeReader>().ReadTextAsync("Pancakes. 2 eggs. Mix and fry.", ct),
            () => sp.GetRequiredService<IRecipeReader>().ReadPhotosAsync([jpeg], ct),
        };
        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<MealIdeaException>(call);
            Assert.Contains("429", ex.Message);
        }
    }
}
