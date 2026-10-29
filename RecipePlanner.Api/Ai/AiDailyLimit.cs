using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Api.Ai;

// The per-user limits in RateLimiting stop one account from running up the AI bill, but accounts are free,
// so many accounts together still could. This is the backstop: a cap on AI calls for the whole app per day.
// The Anthropic Console spend limit caps the bill; this keeps one bad day from using up the whole month.

public class AiDailyLimitOptions
{
    /// <summary>AI calls allowed per UTC day across all users (setting: AiLimits__DailyCalls)</summary>
    public int DailyCalls { get; set; } = 200;
}

/// <summary>One row per AI call. Counted in the database, so the limit holds across restarts and replicas.</summary>
public class AiUsage
{
    public long Id { get; set; }
    public DateTimeOffset UsedAt { get; set; }
}

public class AiDailyLimit(IServiceScopeFactory scopes, TimeProvider clock, IOptions<AiDailyLimitOptions> options,
    ILogger<AiDailyLimit> logger)
{
    /// <summary>Records one AI call and returns true, or returns false if today's limit is already used up.</summary>
    /// <remarks>Two calls landing at the same instant can both squeeze in, so the limit can be passed by a call
    /// or two. That's fine for a cost backstop, and the Console spend limit is the hard ceiling.</remarks>
    public async Task<bool> TryUseAsync(CancellationToken ct)
    {
        // Its own scope: this runs inside HttpClient's handler pipeline, which outlives any one request
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = clock.GetUtcNow();
        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var usedToday = await db.AiUsage.CountAsync(u => u.UsedAt >= startOfDay, ct);
        if (usedToday >= options.Value.DailyCalls)
        {
            logger.LogWarning("Daily AI limit of {Limit} calls reached; AI features are off until midnight UTC",
                options.Value.DailyCalls);
            return false;
        }

        db.AiUsage.Add(new AiUsage { UsedAt = now });
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// Sits in front of every Anthropic HttpClient. Over the daily limit, the request never leaves the app:
/// it gets a 429 back, which the AI classes already turn into "unavailable right now" on the page.
/// </summary>
public class AiDailyLimitHandler(AiDailyLimit limit) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!await limit.TryUseAsync(ct))
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request };
        return await base.SendAsync(request, ct);
    }
}
