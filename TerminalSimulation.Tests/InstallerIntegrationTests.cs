using System.Diagnostics;
using Microsoft.Win32;
using TerminalSimulation.Updater;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class InstallerIntegrationFactAttribute : FactAttribute
{
    public InstallerIntegrationFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("CTS_INSTALLER_QA_PACKAGE"))) Skip = "Set CTS_INSTALLER_QA_PACKAGE to a real QA setup EXE to run installation/uninstallation.";
    }
}
[CollectionDefinition("InstallerIntegration", DisableParallelization = true)]
public sealed class InstallerIntegrationCollection { }

[Collection("InstallerIntegration")]
public sealed class InstallerIntegrationTests
{
    [InstallerIntegrationFact]
    public async Task RealPackage_NativeStage_InstallRepairUninstall_KeepUserFiles()
    {
        var package = Path.GetFullPath(Environment.GetEnvironmentVariable("CTS_INSTALLER_QA_PACKAGE")!);
        var root = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("CTS_INSTALLER_QA_OUTPUT") ?? Path.GetTempPath(), "installer-integration-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var installed = Path.Combine(root, "installed");
        var id = Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable("CTS_INSTALLER_QA_ID");
        Environment.SetEnvironmentVariable("CTS_INSTALLER_QA_ID", id);
        try
        {
            var stage = Path.Combine(root, "staging");
            var manifest = SetupPackage.Read(package);
            var info = new ProcessStartInfo(package) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "--update-stage", "--output", stage, "--expected-version", manifest.Version }) info.ArgumentList.Add(argument);
            using (var process = Process.Start(info)!)
            { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3)); Assert.Equal(0, process.ExitCode); }
            SetupPackage.ValidateDirectory(stage, manifest);
            Assert.False(File.Exists(Path.Combine(stage, ".Installation/install.json")));
            Assert.Null(Registry.CurrentUser.OpenSubKey(@"Software\CarTerminalSimulation\InstallerQA\" + id));
            var result = await InstallationService.InstallAsync(package, installed, true, true, null, CancellationToken.None);
            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(installed, "uninstall.exe")));
            Assert.Equal(manifest.Version, InstallationService.Read(installed)!.Version);
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\CarTerminalSimulation\InstallerQA\" + id)) Assert.Equal(installed, key!.GetValue("InstallLocation"));
            await CloseMain(installed);
            Directory.CreateDirectory(Path.Combine(installed, "Workspaces/Map/new"));
            File.WriteAllText(Path.Combine(installed, "Workspaces/Map/new/sentinel.txt"), "user-route");
            File.WriteAllText(Path.Combine(installed, "config.json"), "{\"qa\":true}");
            Directory.CreateDirectory(Path.Combine(installed, "Plugins"));
            File.WriteAllText(Path.Combine(installed, "Plugins/internal.Plugin"), "keep-plugin");
            File.WriteAllText(Path.Combine(installed, "user-notes.txt"), "unowned");
            // A real repair must heal protected engine damage too, not only app files.
            File.WriteAllText(Path.Combine(installed, ".Updater", manifest.Engine, "update.dll"), "damaged-engine");
            var repair = await InstallationService.InstallAsync(package, installed, true, true, null, CancellationToken.None);
            Assert.True(repair.Success, repair.Message); await CloseMain(installed);
            var removed = await InstallationService.UninstallAsync(installed, null, CancellationToken.None);
            Assert.True(removed.Success, removed.Message);
            Assert.False(File.Exists(Path.Combine(installed, "TerminalSimulation.Wpf.exe")));
            Assert.False(File.Exists(Path.Combine(installed, "uninstall.exe")));
            Assert.Null(InstallationService.Read(installed));
            Assert.Equal("user-route", File.ReadAllText(Path.Combine(installed, "Workspaces/Map/new/sentinel.txt")));
            Assert.Equal("keep-plugin", File.ReadAllText(Path.Combine(installed, "Plugins/internal.Plugin")));
            Assert.Equal("unowned", File.ReadAllText(Path.Combine(installed, "user-notes.txt")));
            Assert.True(File.Exists(Path.Combine(installed, "config.json")));
            Assert.Null(Registry.CurrentUser.OpenSubKey(@"Software\CarTerminalSimulation\InstallerQA\" + id));
        }
        finally
        {
            await CloseMain(installed);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\CarTerminalSimulation\InstallerQA\" + id, false);
            Environment.SetEnvironmentVariable("CTS_INSTALLER_QA_ID", previous);
        }
    }
    private static async Task CloseMain(string installed)
    {
        foreach (var process in Process.GetProcessesByName("TerminalSimulation.Wpf"))
        using (process)
        {
            try
            {
                if (process.MainModule?.FileName != Path.Combine(installed, "TerminalSimulation.Wpf.exe")) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(5000)) process.Kill(true);
                await process.WaitForExitAsync();
            }
            catch (InvalidOperationException) { }
        }
    }
}
