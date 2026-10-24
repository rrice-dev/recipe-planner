using System.Security.Claims;
using System.Threading.RateLimiting;

namespace RecipePlanner.Api.Security;

public static class RateLimiting
{
    // Every client: generous enough that normal use never notices it
    public const int GeneralPermitLimit = 100;
    public static readonly TimeSpan GeneralWindow = TimeSpan.FromMinutes(1);

    // Login, registration and password-reset calls: strict, per IP address
    public const int AuthPermitLimit = 10;
    public static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(5);

    // Only POSTs to these paths count against the strict limit.
    // /manage/info is left out because the web app calls it on every page load.
    private static readonly HashSet<string> SensitiveAuthPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/forgotPassword",
        "/api/auth/resetPassword",
        "/api/auth/resendConfirmationEmail"
    };

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Tell well-behaved clients how long to wait
            o.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                return ValueTask.CompletedTask;
            };

            // Both limiters run on every request; either one can reject it
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GeneralPermitLimit,
                        Window = GeneralWindow,
                        QueueLimit = 0
                    })),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsSensitiveAuthCall(context)
                        ? RateLimitPartition.GetFixedWindowLimiter($"auth:{ClientIp(context)}", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = AuthPermitLimit,
                            Window = AuthWindow,
                            QueueLimit = 0
                        })
                        : RateLimitPartition.GetNoLimiter("unlimited")));
        });

    // Logged-in users get their own bucket; anonymous traffic is grouped by IP
    private static string GeneralKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : $"ip:{ClientIp(context)}";

    // The real client IP once UseForwardedHeaders has run (see Program.cs)
    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool IsSensitiveAuthCall(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) && SensitiveAuthPaths.Contains(context.Request.Path.Value ?? "");
}
