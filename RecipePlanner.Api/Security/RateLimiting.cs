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

    // AI suggestions cost real money per request, so each user gets a small allowance
    public const string AiPolicy = "ai";
    public const int AiPermitLimit = 10;
    public static readonly TimeSpan AiWindow = TimeSpan.FromHours(1);

    // Photo scans get their own allowance, so scanning doesn't use up meal suggestions
    public const string AiScanPolicy = "ai-scan";
    public const int AiScanPermitLimit = 10;

    // Opening a full recipe is a separate, slightly larger allowance: one search can lead to a few of these
    public const string AiRecipePolicy = "ai-recipe";
    public const int AiRecipePermitLimit = 20;

    // Photo uploads use storage, so keep them reasonable
    public const string PhotoUploadPolicy = "photo-upload";
    public const int PhotoUploadPermitLimit = 30;

    // Kroger calls: store search, product matching and cart sends
    // Importing fetches other websites (and may call the AI), so keep it to a sensible pace
    public const string ImportPolicy = "import";
    public const int ImportPermitLimit = 20;

    // Reading a recipe from photos or pasted text: one AI call each, and photos are the priciest kind
    public const string RecipeReadPolicy = "recipe-read";
    public const int RecipeReadPermitLimit = 15;

    // Nutrition can trigger USDA lookups for new ingredient names
    public const string NutritionPolicy = "nutrition";
    public const int NutritionPermitLimit = 60;

    public const string KrogerPolicy = "kroger";
    public const int KrogerPermitLimit = 30;

    // Calls that send an email: stricter still, per IP address. The site's email allowance is small
    // (see Email/EmailCooldowns.cs), so one visitor mustn't be able to spend it.
    public const int EmailPermitLimit = 5;
    public static readonly TimeSpan EmailWindow = TimeSpan.FromHours(1);

    private static readonly HashSet<string> EmailSendingPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/register",
        "/api/auth/forgotPassword",
        "/api/auth/resendConfirmationEmail",
        "/api/account/forgot-password"
    };

    // Only POSTs to these paths count against the strict limit. That's why /manage/info can be listed:
    // the web app GETs it on every page load, which isn't counted; a POST changes the password or email.
    private static readonly HashSet<string> SensitiveAuthPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/forgotPassword",
        "/api/auth/resetPassword",
        "/api/auth/resendConfirmationEmail",
        "/api/auth/manage/info",   // changing your password checks the old one
        "/api/account/delete",     // also checks the password
        "/api/account/forgot-password",
        "/api/account/reset-password"
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

            o.AddPolicy(AiPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AiPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(AiScanPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AiScanPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(AiRecipePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AiRecipePermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(PhotoUploadPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PhotoUploadPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(ImportPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ImportPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(RecipeReadPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RecipeReadPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(NutritionPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = NutritionPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            o.AddPolicy(KrogerPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(GeneralKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = KrogerPermitLimit,
                    Window = AiWindow,
                    QueueLimit = 0
                }));

            // All three limiters run on every request; any one can reject it
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
                        : RateLimitPartition.GetNoLimiter("unlimited")),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsEmailSendingCall(context)
                        ? RateLimitPartition.GetFixedWindowLimiter($"email:{ClientIp(context)}", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = EmailPermitLimit,
                            Window = EmailWindow,
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

    private static bool IsEmailSendingCall(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) && EmailSendingPaths.Contains(context.Request.Path.Value ?? "");
}
