using System.Security.Cryptography;
using System.Text;

namespace TerminalSimulation.Wpf.Services;

internal static class TencentMapKeyProvider
{
    private static readonly Lazy<string> Key = new(DecryptKey, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string GetKey() => Key.Value;

    private static string DecryptKey()
    {
        var wrappingKey = SHA256.HashData(Encoding.UTF8.GetBytes("CTS.Preview9.TencentMap.Key.Wrap.v1"));
        using var aes = Aes.Create();
        aes.Key = wrappingKey;
        aes.IV = EmbeddedTencentMapCredential.InitializationVector;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(
            EmbeddedTencentMapCredential.CipherText,
            0,
            EmbeddedTencentMapCredential.CipherText.Length);
        try
        {
            var key = Encoding.UTF8.GetString(plainBytes).Trim();
            if (key.Length < 16) throw new InvalidOperationException("内置腾讯地图 Key 无效");
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }
}
