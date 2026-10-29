using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Tests;

// Data Protection keys (login cookies, Kroger tokens) are encrypted with Key Vault when it's configured.
// These can't call a real Key Vault; they check the wiring and the clean-up of keys stored before.
public class KeyEncryptionTests
{
    [Fact]
    public void Without_the_setting_keys_are_not_wrapped()
    {
        using var factory = new ApiFactory();
        Assert.Null(factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor);
    }

    [Fact]
    public void With_the_setting_new_keys_are_wrapped_by_key_vault()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(b =>
            b.UseSetting(KeyEncryption.SettingName, "https://example-vault.vault.azure.net/keys/dataprotection/0123456789abcdef"));

        var encryptor = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor;

        Assert.NotNull(encryptor);
        Assert.Contains("AzureKeyVault", encryptor.GetType().Name);
    }

    [Fact]
    public async Task Keys_stored_in_plain_text_are_deleted_and_encrypted_ones_kept()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(await TestDatabase.ConnectionStringAsync(Guid.NewGuid().ToString())).Options);
        db.DataProtectionKeys.AddRange(
            new DataProtectionKey { FriendlyName = "plain", Xml = "<key><descriptor><masterKey><value>c2VjcmV0</value></masterKey></descriptor></key>" },
            new DataProtectionKey { FriendlyName = "wrapped", Xml = "<key><descriptor><encryptedSecret decryptorType=\"x\"><encryptedKey /></encryptedSecret></descriptor></key>" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await KeyEncryption.DeleteUnencryptedKeysAsync(db));
        Assert.Equal(["wrapped"], await db.DataProtectionKeys.Select(k => k.FriendlyName!).ToListAsync());
    }
}
