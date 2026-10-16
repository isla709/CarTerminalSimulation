using System.Text.Json;
using TerminalSimulation.Updater;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class UpdateDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CTS-DeploymentTests", Guid.NewGuid().ToString("N"));
    private string Installation => Path.Combine(_root, "installed");
    private string Payload => Path.Combine(_root, "payload");
    private string Backup => Path.Combine(_root, "backup");
    private UpdatePlan Plan => new() { TargetDirectory = Installation, MainExecutablePath = Path.Combine(Installation, "app.exe") };

    [Fact]
    public async Task SelfContainedToFrameworkDependent_RemovesOnlyObsoleteRuntimeAndBacksUp()
    {
        Setup();
        await Apply();
        foreach (var name in new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Runtime.dll" })
        {
            Assert.False(File.Exists(Path.Combine(Installation, name)));
            Assert.Equal("old-" + name, File.ReadAllText(Path.Combine(Backup, "deleted", name)));
        }
        Assert.Equal("new-json", File.ReadAllText(Path.Combine(Installation, "System.Text.Json.dll")));
        Assert.Equal("settings", File.ReadAllText(Path.Combine(Installation, "config.json")));
        Assert.Equal("routes", File.ReadAllText(Path.Combine(Installation, "Workspaces/Map/new/history.json")));
        Assert.Equal("custom", File.ReadAllText(Path.Combine(Installation, "Custom.Plugin")));
    }

    [Fact]
    public async Task CleanupFailure_RollsBackRuntimeAndPayload()
    {
        Setup();
        using var locked = new FileStream(Path.Combine(Installation, "hostpolicy.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => Apply());
        Assert.Equal("old-hostfxr.dll", File.ReadAllText(Path.Combine(Installation, "hostfxr.dll")));
        Assert.Equal("old-coreclr.dll", File.ReadAllText(Path.Combine(Installation, "coreclr.dll")));
        Assert.Equal("old-app", File.ReadAllText(Path.Combine(Installation, "app.exe")));
        Assert.Contains("includedFrameworks", File.ReadAllText(Path.Combine(Installation, "app.runtimeconfig.json")));
        Assert.False(File.Exists(Path.Combine(Installation, "new-file.txt")));
    }

    [Fact]
    public async Task CancellationBeforeCleanup_RollsBackPayload()
    {
        Setup();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateEngine.ApplyWithRollbackAsync(Plan, Payload, Backup,
                new CallbackProgress(_ => cancellation.Cancel()), cancellation.Token));
        Assert.Equal("old-app", File.ReadAllText(Path.Combine(Installation, "app.exe")));
        Assert.True(File.Exists(Path.Combine(Installation, "hostfxr.dll")));
    }

    [Fact]
    public void SelfContainedTarget_DoesNotRemoveItsRuntime()
    {
        Setup();
        Write(Payload, "app.runtimeconfig.json", """{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App"}]}}""");
        Assert.Empty(UpdateDeploymentPlanner.GetObsoleteRuntimeFiles(Installation, Payload, Plan.MainExecutablePath));
    }

    [Fact]
    public void AlreadyMixedInstall_RemovesStaleHostsEvenWithoutRuntimePackMetadata()
    {
        Setup();
        Write(Installation, "app.deps.json", """{"runtimeTarget":{"name":"test"},"targets":{"test":{}}}""");
        var removed = UpdateDeploymentPlanner.GetObsoleteRuntimeFiles(Installation, Payload, Plan.MainExecutablePath);
        Assert.Equal(new[] { "hostfxr.dll", "hostpolicy.dll" }, removed);
    }

    [Fact]
    public void PayloadProvidedFile_IsNeverAutomaticallyDeleted()
    {
        Setup();
        Write(Payload, "coreclr.dll", "explicit-payload");
        Assert.DoesNotContain("coreclr.dll", UpdateDeploymentPlanner.GetObsoleteRuntimeFiles(Installation, Payload, Plan.MainExecutablePath));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("C:/outside.dll")]
    public async Task InvalidOldRuntimeAsset_RejectsBeforeAnyOverwrite(string asset)
    {
        Setup([asset]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Apply());
        Assert.Equal("old-app", File.ReadAllText(Path.Combine(Installation, "app.exe")));
    }

    private Task Apply() => UpdateEngine.ApplyWithRollbackAsync(Plan, Payload, Backup, null, CancellationToken.None);

    private void Setup(string[]? runtimeAssets = null)
    {
        Write(Installation, "app.exe", "old-app");
        Write(Installation, "app.runtimeconfig.json", """{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App"}]}}""");
        Write(Installation, "app.deps.json", JsonSerializer.Serialize(new {
            runtimeTarget = new { name = "test" },
            targets = new Dictionary<string, object> {
                ["test"] = new Dictionary<string, object> {
                    ["runtimepack.Microsoft.NETCore.App.Runtime.win-x64/8.0.28"] = new {
                        runtime = (runtimeAssets ?? ["System.Runtime.dll", "System.Text.Json.dll"]).ToDictionary(x => x, _ => new { }),
                        native = new Dictionary<string, object> { ["coreclr.dll"] = new { } }
                    }
                }
            }
        }));
        foreach (var name in new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Runtime.dll", "System.Text.Json.dll" })
            Write(Installation, name, "old-" + name);
        Write(Installation, "config.json", "settings");
        Write(Installation, "Workspaces/Map/new/history.json", "routes");
        Write(Installation, "Custom.Plugin", "custom");
        Write(Payload, "app.exe", "new-app");
        Write(Payload, "app.runtimeconfig.json", """{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"}]}}""");
        Write(Payload, "System.Text.Json.dll", "new-json");
        Write(Payload, "new-file.txt", "new");
    }

    private static void Write(string root, string name, string text)
    {
        var file = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    private sealed class CallbackProgress(Action<UpdateProgress> callback) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) => callback(value);
    }
}
