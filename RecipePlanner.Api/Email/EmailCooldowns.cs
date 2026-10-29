using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Api.Email;

// The site sends from Azure's free azurecomm.net address, which allows only 10 emails an hour for the whole
// Azure subscription. Without a limit per account, someone could ask for reset or confirmation emails over
// and over (for their own account or anyone's) and use up that allowance, so real resets and confirmations
// would fail for everyone. Each account gets at most one email of each kind per 15 minutes; the request
// still answers the same way, so this reveals nothing about which addresses have accounts.
// Sending from your own domain raises Azure's limit to 100 an hour (and keeps mail out of spam folders).
public sealed class EmailCooldowns(IServiceScopeFactory scopes, TimeProvider clock)
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(15);
    public const string Confirmation = "confirm";
    public const string Reset = "reset";

    /// <summary>True (and the time is recorded) if this account may be sent this kind of email now</summary>
    public async Task<bool> TryClaimAsync(string userId, string kind)
    {
        // Its own scope: Identity calls the email sender from inside its own request handling
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var cutoff = now - Wait;

        // One statement, so two requests at the same moment can't both get through (and it works across replicas):
        // a new row, or an old one past the wait, is claimed; a recent one returns nothing
        var claimed = await db.Database.SqlQuery<int>($"""
            INSERT INTO "EmailCooldowns" ("UserId", "Kind", "LastSentAt") VALUES ({userId}, {kind}, {now})
            ON CONFLICT ("UserId", "Kind") DO UPDATE SET "LastSentAt" = EXCLUDED."LastSentAt"
            WHERE "EmailCooldowns"."LastSentAt" <= {cutoff}
            RETURNING 1 AS "Value"
            """).ToListAsync();
        return claimed.Count > 0;
    }
}
