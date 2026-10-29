using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Email;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Security;

// AI features cost money per call, and accounts are free to make. Requiring a confirmed email means each
// account that can use AI is tied to a real inbox, so making many accounts to multiply the per-user AI
// allowance takes real effort. Without email set up (local development) there's no way to confirm, so
// the check is skipped.
public static class ConfirmedEmail
{
    public const string ProblemTitle = "email_not_confirmed";

    public static TBuilder RequireConfirmedEmail<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!http.RequestServices.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured)
                return await next(context);

            var users = http.RequestServices.GetRequiredService<UserManager<AppUser>>();
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            if (user.EmailConfirmed) return await next(context);

            return Results.Problem(
                "Confirm your email to use AI features. Check your inbox for the link, or send a new one.",
                statusCode: StatusCodes.Status403Forbidden, title: ProblemTitle);
        });
}
