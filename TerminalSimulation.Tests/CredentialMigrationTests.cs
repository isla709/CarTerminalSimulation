using System.Security.Cryptography;
using System.Text;
using TerminalSimulation.Plugins.XunjieCloud.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class CredentialMigrationTests
{
    [Fact]
    public void Dpapi_CurrentUserRoundTripSucceeds()
    {
        var encrypted = UtilitySettingsManager.Encrypt("secret-password");
        Assert.NotEqual("secret-password", encrypted);
        Assert.True(UtilitySettingsManager.TryDecrypt(encrypted, 2, out var decrypted));
        Assert.Equal("secret-password", decrypted);
    }

    [Fact]
    public void InvalidCiphertextFailsWithoutReturningPlaintext()
    {
        Assert.False(UtilitySettingsManager.TryDecrypt("not-base64", 2, out var decrypted));
        Assert.Empty(decrypted);
    }

    [Fact]
    public void LegacyAesCiphertextCanBeReadForMigration()
    {
        const string password = "legacy-password";
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes("TerminalSimUtili808AESKey123456!");
        aes.IV = Encoding.UTF8.GetBytes("TerminalSimIV123");
        using var output = new MemoryStream();
        using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            crypto.Write(Encoding.UTF8.GetBytes(password));
        }

        Assert.True(UtilitySettingsManager.TryDecrypt(Convert.ToBase64String(output.ToArray()), 0, out var decrypted));
        Assert.Equal(password, decrypted);
        Assert.True(UtilitySettingsManager.TryDecrypt(UtilitySettingsManager.Encrypt(decrypted), 2, out var migrated));
        Assert.Equal(password, migrated);
    }
}
