using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TerminalSimulation.Shared;

internal static class WindowsAppIdentity
{
    // Keep shell identity stable across version changes and updater worker paths.
    // Main app and updater intentionally use different taskbar groups.
    internal static void Initialize(string appUserModelId)
    {
        var result = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        if (result < 0)
            Debug.WriteLine($"Unable to set Windows app identity: 0x{result:X8}");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
