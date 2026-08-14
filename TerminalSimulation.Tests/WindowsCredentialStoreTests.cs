using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void ProtectAndUnprotect_UsesCurrentWindowsUser()
    {
        ICredentialStore store = new WindowsCredentialStore();
        var protectedValue = store.Protect("credential-value");
        Assert.NotEqual("credential-value", protectedValue);
        Assert.True(store.TryUnprotect(protectedValue, out var value));
        Assert.Equal("credential-value", value);
    }

    [Fact]
    public void TryUnprotect_InvalidPayloadReturnsFalse()
    {
        ICredentialStore store = new WindowsCredentialStore();
        Assert.False(store.TryUnprotect("invalid", out var value));
        Assert.Empty(value);
    }
}
