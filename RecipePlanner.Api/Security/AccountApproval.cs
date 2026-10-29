using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Security;

// This is a personal site: anyone can fill in the sign-up form, but the new account can't sign in until an
// admin approves it (Settings → "Accounts waiting for approval", or /api/admin/users). Turn it off for local
// development with Registration__RequireApproval=false.
public class RegistrationOptions
{
    public bool RequireApproval { get; set; } = true;
}

/// <summary>
/// Identity's sign-in, plus "not approved yet" counts as not allowed (login answers 401 "NotAllowed").
/// Checked only after the password is right: checking first would tell anyone typing an email that it's a
/// waiting account, and wrong guesses wouldn't count toward the lockout.
/// </summary>
public class ApprovalSignInManager(
    UserManager<AppUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<AppUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<AppUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation,
    IOptions<RegistrationOptions> registration)
    : SignInManager<AppUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<SignInResult> CheckPasswordSignInAsync(AppUser user, string password, bool lockoutOnFailure)
    {
        var result = await base.CheckPasswordSignInAsync(user, password, lockoutOnFailure);
        return result.Succeeded && registration.Value.RequireApproval && !user.IsApproved ? SignInResult.NotAllowed : result;
    }
}
