using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Api.Security;

// The Data Protection keys sign login cookies and encrypt Kroger tokens and reset links. They're stored in
// the database so every container shares them. Without a key encryption key they're stored in plain text,
// right next to the data they protect: anyone who could read the database could forge a login for any
// account and decrypt Kroger tokens.
//
// With DataProtection:KeyVaultKeyId set (env var DataProtection__KeyVaultKeyId), each key is encrypted with
// a Key Vault key before it's stored, and only the API's managed identity can unwrap it. A database copy on
// its own is then useless. Setup: docs/key-encryption.md.
public static class KeyEncryption
{
    public const string SettingName = "DataProtection:KeyVaultKeyId";

    public static IDataProtectionBuilder ProtectKeysIfConfigured(this IDataProtectionBuilder dataProtection, IConfiguration configuration)
    {
        if (configuration[SettingName] is { Length: > 0 } keyId)
            dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyId), new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
        return dataProtection;
    }

    /// <summary>
    /// Once keys are encrypted, any key stored before that is still readable plain text, so it's deleted.
    /// Runs at startup, before anything uses the keys. The one-time cost: everyone is signed out, and Kroger
    /// connections need reconnecting (their tokens were encrypted with the deleted key).
    /// </summary>
    public static async Task<int> DeleteUnencryptedKeysAsync(AppDbContext db)
    {
        // An encrypted key's XML holds <encryptedSecret>; a plain one holds the secret itself
        var plain = await db.DataProtectionKeys.Where(k => k.Xml == null || !k.Xml.Contains("<encryptedSecret")).ToListAsync();
        db.DataProtectionKeys.RemoveRange(plain);
        await db.SaveChangesAsync();
        return plain.Count;
    }
}
