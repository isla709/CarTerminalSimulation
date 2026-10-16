using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerminalSimulation.Updater;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class SetupPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CTS-SetupTests", Guid.NewGuid().ToString("N"));
    private string Payload => Path.Combine(_root, "payload");
    private string Package => Path.Combine(_root, "setup.exe");
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public SetupPackageTests()
    {
        Directory.CreateDirectory(_root);
        foreach (var file in new[] { "TerminalSimulation.Wpf.exe", "TerminalSimulation.Wpf.dll", "update.exe", "update.dll", "update.deps.json", "update.runtimeconfig.json",
            ".Updater/compat-host.exe", ".Updater/engines/3.1.0/update.exe", ".Updater/engines/3.1.0/update.dll", ".Updater/engines/3.1.0/update.deps.json", ".Updater/engines/3.1.0/update.runtimeconfig.json" })
        { var path = Path.Combine(Payload, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "MZ-fake-test-asset"); }
        File.WriteAllText(Path.Combine(Payload, ".Updater/current.txt"), "engines/3.1.0");
        File.WriteAllText(Path.Combine(Payload, ".Updater/last-good.txt"), "engines/3.1.0");
        var bundle = new BundleManifest { SchemaVersion = 1, KernelVersion = "3.1.0", Engine = "engines/3.1.0", EntrySha256 = Hash(Path.Combine(Payload, "update.exe")) };
        foreach (var file in Directory.GetFiles(Path.Combine(Payload, ".Updater"), "*", SearchOption.AllDirectories).Where(x => x.EndsWith(".exe") || x.EndsWith(".dll") || x.EndsWith(".json")))
            bundle.Files[Path.GetRelativePath(Path.Combine(Payload, ".Updater"), file).Replace('\\', '/')] = Hash(file);
        File.WriteAllText(Path.Combine(Payload, ".Updater/bundle.json"), JsonSerializer.Serialize(bundle, UpdaterJsonContext.Default.BundleManifest));
        File.WriteAllBytes(Path.Combine(_root, "stub.exe"), [(byte)'M', (byte)'Z', .. new byte[1022]]);
    }
    private void Build() => SetupPackage.Create(Payload, Path.Combine(_root, "stub.exe"), Package, "preview11-test", "main", 1);
    internal string BuildTestPackage() { Build(); return Package; }

    [Fact]
    public void OneExecutable_IsAlsoReadableByLegacyZipExtraction_AndAllBytesMatch()
    {
        Build(); Assert.True(SetupPackage.IsExecutable(Package));
        var meta = SetupPackage.Read(Package); Assert.Equal("preview11-test", meta.Version);
        var stage = Path.Combine(_root, "legacy"); Directory.CreateDirectory(stage);
        UpdateEngine.ExtractZipSafely(Package, stage);
        SetupPackage.ValidateDirectory(stage, meta);
        Assert.Equal(Hash(Path.Combine(Payload, "TerminalSimulation.Wpf.exe")), Hash(Path.Combine(stage, "TerminalSimulation.Wpf.exe")));
        using var archive = ZipFile.OpenRead(Package);
        foreach (var file in archive.Entries.Where(x => x.FullName.StartsWith(".Updater/engines/3.1.0/"))) Assert.Equal(file.Length, file.CompressedLength);
    }
    [Fact]
    public void LegacyRuntimeBridge_IsPreservedOnlyWhenItPointsInsideThePrivateRuntime()
    {
        var runtime = Path.Combine(Payload, ".Updater/runtimes/net8-x64");
        var host = Path.Combine(runtime, "host/fxr/8.0.32/hostfxr.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(host)!);
        File.WriteAllText(host, "fake-hostfxr");
        File.WriteAllText(Path.Combine(Payload, ".Updater/app-runtime.txt"), ".Updater/runtimes/net8-x64");
        File.WriteAllText(Path.Combine(Payload, ".Updater/app-hostfxr.txt"), ".Updater/runtimes/net8-x64/host/fxr/8.0.32/hostfxr.dll");

        Build();
        var manifest = SetupPackage.Read(Package);
        Assert.Contains(".Updater/app-runtime.txt", manifest.Files.Keys);
        Assert.Contains(".Updater/app-hostfxr.txt", manifest.Files.Keys);
        var stage = Path.Combine(_root, "bridge-stage");
        SetupPackage.Extract(Package, stage, "preview11-test");
        Assert.Equal(".Updater/runtimes/net8-x64", File.ReadAllText(Path.Combine(stage, ".Updater/app-runtime.txt")));
    }
    [Theory]
    [InlineData("C:/outside")]
    [InlineData("../outside")]
    public void LegacyRuntimeBridge_RejectsPathsOutsideThePackage(string runtimePath)
    {
        var runtime = Path.Combine(Payload, ".Updater/runtimes/net8-x64");
        var host = Path.Combine(runtime, "host/fxr/8.0.32/hostfxr.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(host)!);
        File.WriteAllText(host, "fake-hostfxr");
        File.WriteAllText(Path.Combine(Payload, ".Updater/app-runtime.txt"), runtimePath);
        File.WriteAllText(Path.Combine(Payload, ".Updater/app-hostfxr.txt"), ".Updater/runtimes/net8-x64/host/fxr/8.0.32/hostfxr.dll");
        Assert.Throws<InvalidDataException>(Build);
    }
    [Fact]
    public void StageModeRejectsWrongVersionNonEmptyOutputAndTampering()
    {
        Build(); var stage = Path.Combine(_root, "staging");
        Assert.Throws<InvalidDataException>(() => SetupPackage.Extract(Package, stage, "wrong-version"));
        SetupPackage.Extract(Package, stage, "preview11-test");
        Assert.Throws<IOException>(() => SetupPackage.Extract(Package, stage));
        File.WriteAllText(Path.Combine(stage, "TerminalSimulation.Wpf.dll"), "tampered");
        Assert.Throws<InvalidDataException>(() => SetupPackage.ValidateDirectory(stage, SetupPackage.Read(Package)));
    }
    [Theory]
    [InlineData("config.json")][InlineData("utility_settings.json")][InlineData("Workspaces/Map/history.json")][InlineData("Plugins/internal.Plugin")]
    [InlineData("KEY.txt")][InlineData(".Installation/install.json")][InlineData(".Updater/state.json")][InlineData("XunjieCloud.dll")]
    [InlineData(".Updater/update.lock")]
    public void PackagingRefusesUserDataOrInternalPlugin(string file)
    {
        var path = Path.Combine(Payload, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "private");
        Assert.Throws<InvalidDataException>(Build);
    }
    [Fact]
    public void RefusesPlainExecutableOrPlainZipMasqueradingAsSetup()
    {
        Assert.Throws<InvalidDataException>(() => SetupPackage.Read(Path.Combine(_root, "stub.exe")));
        ZipFile.CreateFromDirectory(Payload, Path.Combine(_root, "plain.zip"));
        Assert.Throws<InvalidDataException>(() => SetupPackage.Read(Path.Combine(_root, "plain.zip")));
    }
    [Fact]
    public void TargetDirectoryValidationRefusesRootSourceAndUnrelatedUserFolders()
    {
        Assert.Throws<InvalidDataException>(() => InstallationService.ValidateTarget(Path.GetPathRoot(_root)!));
        Assert.Throws<InvalidDataException>(() => InstallationService.ValidateTarget(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        var unrelated = Path.Combine(_root, "user"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "notes.txt"), "keep");
        Assert.Throws<InvalidDataException>(() => InstallationService.ValidateTarget(unrelated));
        Assert.Equal(Path.Combine(_root, "new", "program"), InstallationService.ValidateTarget(Path.Combine(_root, "new", "program")));
    }
    [Theory]
    [InlineData("Workspaces/Map/new/history.json")][InlineData("Plugins/internal.Plugin")][InlineData("config.json")]
    [InlineData("utility_settings.json")][InlineData("update-sources.json")][InlineData(".Updater/transactions/id/backup/app.exe")]
    public void UninstallAlwaysProtectsUserDataAndRecoveryBackups(string path) => Assert.True(InstallationService.IsUserData(path));
    [Theory]
    [InlineData(".Installation/install.json")][InlineData("uninstall.exe")]
    public async Task OldPayloadDoesNotRemoveInstallationIdentity(string path)
    {
        var installed = Path.Combine(_root, "installed"); var full = Path.Combine(installed, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, "retained");
        await UpdateEngine.ApplyWithRollbackAsync(new() { TargetDirectory = installed, MainExecutablePath = Path.Combine(installed, "TerminalSimulation.Wpf.exe"), Delete = [path] }, Payload, Path.Combine(_root, "backup"), null, CancellationToken.None);
        Assert.Equal("retained", File.ReadAllText(full));
    }
    [Fact]
    public void InterruptedUninstallRestoresJournalBeforeNextInstallation()
    {
        var target = Path.Combine(_root, "installed"); Directory.CreateDirectory(target);
        var main = Path.Combine(target, "app.exe"); File.WriteAllText(main, "original");
        var backup = Path.Combine(target, ".Updater/transactions/test/backup");
        var transaction = new FileTransaction(target, backup); transaction.Delete("app.exe");
        var marker = Path.Combine(target, ".Installation/uninstall-pending.txt"); Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, Path.GetRelativePath(target, backup));
        InstallationService.RecoverInterruptedUninstall(target);
        Assert.Equal("original", File.ReadAllText(main)); Assert.False(File.Exists(marker));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
