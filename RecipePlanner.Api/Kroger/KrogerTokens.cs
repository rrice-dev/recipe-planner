using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Kroger;

// Kroger tokens let us change someone's cart, so they're encrypted with the app's Data Protection keys
// before they touch the database. A database leak alone doesn't expose them.
public class KrogerTokens(IDataProtectionProvider protection, KrogerClient kroger, ILogger<KrogerTokens> logger)
{
    private readonly IDataProtector _protector = protection.CreateProtector("RecipePlanner.Kroger.Tokens.v1");

    public void Store(KrogerConnection connection, KrogerToken token)
    {
        if (token.RefreshToken is not null)
            connection.EncryptedRefreshToken = _protector.Protect(token.RefreshToken);
        connection.EncryptedAccessToken = _protector.Protect(token.AccessToken);
        connection.AccessTokenExpiresAt = token.ExpiresAt;
    }

    public string Encrypt(string value) => _protector.Protect(value);

    // A valid access token for this user's Kroger account, refreshing it if needed.
    // Null means the user has to connect again (Kroger revoked or expired the refresh token).
    public async Task<string?> GetAccessTokenAsync(AppDbContext db, KrogerConnection connection, CancellationToken ct)
    {
        if (connection.EncryptedAccessToken is not null && connection.AccessTokenExpiresAt > DateTimeOffset.UtcNow)
        {
            try { return _protector.Unprotect(connection.EncryptedAccessToken); }
            catch (System.Security.Cryptography.CryptographicException) { /* fall through and refresh */ }
        }

        try
        {
            var refreshed = await kroger.RefreshAsync(_protector.Unprotect(connection.EncryptedRefreshToken), ct);
            Store(connection, refreshed);   // Kroger may hand out a new refresh token too
            await db.SaveChangesAsync(ct);
            return refreshed.AccessToken;
        }
        catch (Exception ex) when (ex is KrogerException { Status: 400 or 401 } or System.Security.Cryptography.CryptographicException)
        {
            logger.LogInformation("Kroger connection for a user is no longer valid; removing it");
            db.KrogerConnections.Remove(connection);
            await db.SaveChangesAsync(ct);
            return null;
        }
    }
}
