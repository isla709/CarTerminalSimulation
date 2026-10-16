using System.Text.Json;
using TerminalSimulation.Updater;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class UpdateKernelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CTS-KernelTests", Guid.NewGuid().ToString("N"));
    private string Installed => Path.Combine(_root, "installed");
    private string Payload => Path.Combine(_root, "payload");
    private string Backup => Path.Combine(_root, "backup");

    [Theory]
    [InlineData("update.exe")]
    [InlineData("UPDATE.DLL")]
    [InlineData("update.deps.json")]
    [InlineData("update.runtimeconfig.json")]
    [InlineData(".Updater/engines/3.0.0/update.exe")]
    [InlineData(".Updater/state.json")]
    [InlineData("Workspaces/Http/.Save/workspace.json")]
    [InlineData("Plugins/internal.Plugin")]
    [InlineData("config.json")]
    [InlineData("update-sources.json")]
    public async Task OldPayloadCannotOverwriteOrDeleteProtectedFiles(string path)
    {
        Write(Installed, path, "retained");
        Write(Payload, path, "legacy");
        Write(Payload, "app.dll", "new-app");
        var plan = new UpdatePlan { TargetDirectory = Installed, MainExecutablePath = Path.Combine(Installed, "app.exe"), Delete = [path] };
        await UpdateEngine.ApplyWithRollbackAsync(plan, Payload, Backup, null, CancellationToken.None);
        Assert.Equal("retained", File.ReadAllText(Path.Combine(Installed, path)));
        Assert.Equal("new-app", File.ReadAllText(Path.Combine(Installed, "app.dll")));
    }

    [Fact]
    public void HighWaterEpochCannotBeBypassedByAnOldMainProgram()
    {
        var state = new KernelState { Line = "main", HighestCompatibilityEpoch = 2 };
        var plan = new UpdatePlan { CurrentLine = "main", TargetLine = "main", CurrentCompatibilityEpoch = 1, TargetCompatibilityEpoch = 1 };
        Assert.Throws<InvalidDataException>(() => UpdateKernel.ValidateState(plan, state));
        plan.TargetCompatibilityEpoch = 2;
        UpdateKernel.ValidateState(plan, state);
        plan.TargetLine = "other";
        Assert.Throws<InvalidDataException>(() => UpdateKernel.ValidateState(plan, state));
    }

    [Fact]
    public void TransactionRestoresFirstSnapshotAfterMultipleWrites()
    {
        Write(Installed, "app.exe", "original");
        Write(Payload, "app.exe", "payload");
        var transaction = new FileTransaction(Installed, Backup);
        transaction.Replace(Path.Combine(Payload, "app.exe"), "app.exe");
        transaction.Write("app.exe", "compatibility-host");
        transaction.Write("new.dll", "new");
        transaction.Rollback();
        Assert.Equal("original", File.ReadAllText(Path.Combine(Installed, "app.exe")));
        Assert.False(File.Exists(Path.Combine(Installed, "new.dll")));
    }

    [Fact]
    public void InterruptedTransactionCanBeRecoveredByNextEngine()
    {
        Write(Installed, "app.dll", "old");
        var relative = ".Updater/transactions/test/backup";
        var transaction = new FileTransaction(Installed, Path.Combine(Installed, relative));
        transaction.Write("app.dll", "new");
        transaction.Write("added.dll", "new");
        FileTransaction.Recover(Installed, relative);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "app.dll")));
        Assert.False(File.Exists(Path.Combine(Installed, "added.dll")));
        Assert.Throws<InvalidDataException>(() => FileTransaction.Recover(Installed, "../outside"));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../outside")]
    [InlineData("C:/outside")]
    [InlineData("file.dll:stream")]
    public void ComponentPathsCannotEscapeTheirRoot(string path) => Assert.Throws<InvalidDataException>(() => UpdateKernel.SafePath(Installed, path));

    [Fact]
    public void RuntimeResolverRejectsWrongMajorAndHonorsDisableRollForward()
    {
        Write(Installed, "shared/Microsoft.NETCore.App/10.0.0/placeholder", "");
        Write(Installed, "shared/Microsoft.WindowsDesktop.App/10.0.0/placeholder", "");
        Write(Installed, "host/fxr/10.0.0/hostfxr.dll", "");
        using var normal = JsonDocument.Parse("{}");
        (string, Version)[] required = [("Microsoft.NETCore.App", new Version(8, 0, 0)), ("Microsoft.WindowsDesktop.App", new Version(8, 0, 0))];
        Assert.Null(RuntimeAdapter.FindCompatible(Installed, required, normal.RootElement));
        Write(Installed, "shared/Microsoft.NETCore.App/8.0.32/placeholder", "");
        Write(Installed, "shared/Microsoft.WindowsDesktop.App/8.0.32/placeholder", "");
        Write(Installed, "host/fxr/8.0.32/hostfxr.dll", "");
        Assert.NotNull(RuntimeAdapter.FindCompatible(Installed, required, normal.RootElement));
        using var exact = JsonDocument.Parse("""{"rollForward":"Disable"}""");
        Assert.Null(RuntimeAdapter.FindCompatible(Installed, required, exact.RootElement));
    }

    [Fact]
    public void MissingSelfContainedRuntimeFailsBeforeChangingInstallation()
    {
        foreach (var extension in new[] { ".exe", ".dll" }) Write(Payload, "app" + extension, "target");
        Write(Payload, "app.deps.json", """{"runtimeTarget":{"name":".NETCoreApp,Version=v8.0/win-x64"}}""");
        Write(Payload, "app.runtimeconfig.json", """{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"}]}}""");
        var plan = new UpdatePlan { TargetDirectory = Installed, MainExecutablePath = Path.Combine(Installed, "app.exe") };
        Assert.Throws<InvalidDataException>(() => RuntimeAdapter.Preflight(plan, Payload));
        Assert.False(Directory.Exists(Installed));
    }

    [Fact]
    public void FallbackKernelCanIgnoreAnInactiveCorruptCandidate()
    {
        var bundle = new BundleManifest { SchemaVersion = 1, KernelVersion = "4.0.0", Engine = "engines/4.0.0" };
        foreach (var name in new[] { "engines/3.0.0/update.exe", "engines/3.0.0/update.dll", "engines/4.0.0/update.exe", "engines/4.0.0/update.dll", "compat-host.exe" })
        {
            Write(UpdateKernel.Root(Installed), name, "healthy");
            bundle.Files[name] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("healthy")));
        }
        Write(UpdateKernel.Root(Installed), "bundle.json", JsonSerializer.Serialize(bundle, UpdaterJsonContext.Default.BundleManifest));
        Write(UpdateKernel.Root(Installed), "engines/4.0.0/update.exe", "corrupt");
        Assert.Throws<InvalidDataException>(() => UpdateKernel.ValidateBundle(Installed));
        Assert.NotNull(UpdateKernel.ValidateBundle(Installed, "engines/3.0.0"));
    }

    [Fact]
    public void NewKernelManifestRetainsLastGoodAndPrivateRuntimeHashes()
    {
        var retained = new BundleManifest { Files = new() { ["engines/3.0.0/update.exe"] = "old-engine", ["runtimes/net8-x64/coreclr.dll"] = "runtime", ["compat-host.exe"] = "old-host" } };
        var incoming = new BundleManifest { KernelVersion = "4.0.0", Files = new() { ["engines/4.0.0/update.exe"] = "new-engine", ["compat-host.exe"] = "new-host" } };
        var combined = UpdateKernel.MergeBundle(retained, incoming);
        Assert.Equal("old-engine", combined.Files["engines/3.0.0/update.exe"]);
        Assert.Equal("runtime", combined.Files["runtimes/net8-x64/coreclr.dll"]);
        Assert.Equal("new-host", combined.Files["compat-host.exe"]);
        Assert.Equal("4.0.0", combined.KernelVersion);
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
