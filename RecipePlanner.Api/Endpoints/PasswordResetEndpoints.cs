using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Email;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

// Identity's built-in /forgotPassword and /resetPassword only work for accounts with a confirmed email.
// Confirming is optional here (it's only needed for AI features), so someone who never confirmed could
// never reset. These versions send the link to any registered address. That's safe: only whoever reads that
// inbox gets the link. A successful reset proves they own the address, so it's marked confirmed.
public static class PasswordResetEndpoints
{
    public static void MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        // Both paths are on the strict login rate limit (RateLimiting.SensitiveAuthPaths)
        app.MapPost("/api/account/forgot-password", async (ForgotPasswordRequest request, UserManager<AppUser> users,
            IEmailSender<AppUser> sender, IOptions<EmailOptions> email, ILogger<ForgotPasswordRequest> logger) =>
        {
            if (!email.Value.IsConfigured) return Results.NotFound();
            var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
            if (user?.Email is not null)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                await sender.SendPasswordResetCodeAsync(user, user.Email, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));
                // The sender skips it if this account was sent one in the last 15 minutes (see EmailCooldowns)
                logger.LogInformation("Password reset asked for user {UserId}", user.Id);
            }
            else
            {
                // Only in the server log, never in the response; no address is logged
                logger.LogInformation("Password reset asked for an address with no account");
            }
            // The same answer either way, so this can't be used to find out who has an account
            return Results.Ok();
        });

        app.MapPost("/api/account/reset-password", async (ResetPasswordRequest request, UserManager<AppUser> users) =>
        {
            var invalid = Results.ValidationProblem(new Dictionary<string, string[]>
                { ["Code"] = ["That reset link is invalid or has expired. Ask for a new one."] });

            var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || string.IsNullOrWhiteSpace(request.Code)) return invalid;

            string token;
            try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code)); }
            catch (FormatException) { return invalid; }

            var result = await users.ResetPasswordAsync(user, token, request.NewPassword ?? "");
            if (!result.Succeeded)
            {
                // Password rule problems are worth showing; a bad token gets the generic message
                var rules = result.Errors.Where(e => e.Code.StartsWith("Password")).Select(e => e.Description).ToArray();
                return rules.Length > 0 ? Results.ValidationProblem(new Dictionary<string, string[]> { ["NewPassword"] = rules }) : invalid;
            }

            // Using the emailed link proves they own the address, and a lockout from forgotten-password guesses can end
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            return Results.Ok();
        });
    }
}
