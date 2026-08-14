using System.Security.Cryptography;
using System.Text;

namespace TerminalSimulation.Wpf.Services;

internal sealed class WindowsCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CarTerminalSimulation.Credentials.v1");

    public string Protect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    }

    public bool TryUnprotect(string protectedValue, out string value)
    {
        value = string.Empty;
        try
        {
            value = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return false; }
    }
}
