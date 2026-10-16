using System.Text.Json;
using TerminalSimulation.Updater;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class UpdaterBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CTS-BootstrapTests", Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Worker => Path.Combine(_root, "worker");

    [Fact]
    public void FrameworkDependent_CopiesOnlyUpdaterDependencies()
    {
        CreateSource(false, ["update.dll"]);
        Write("TerminalSimulation.Wpf.dll");
        Write("KEY.txt");
        Write("config.json");
        Write("Workspaces/Map/new/history.json");
        UpdaterBootstrap.PrepareWorkerFiles(Source, Worker);
        Assert.Equal(4, Directory.GetFiles(Worker, "*", SearchOption.AllDirectories).Length);
        Assert.False(File.Exists(Path.Combine(Worker, "KEY.txt")));
        Assert.False(Directory.Exists(Path.Combine(Worker, "Workspaces")));
    }

    [Fact]
    public void FrameworkDependent_CopiesPrivateRuntimeForDetachedWorker()
    {
        CreateSource(false, ["update.dll"]);
        var privateRuntime = Path.Combine(_root, "private-runtime");
        var hostfxr = Path.Combine(privateRuntime, "host", "fxr", "8.0.32", "hostfxr.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(hostfxr)!);
        File.WriteAllText(hostfxr, "private-hostfxr");
        UpdaterBootstrap.PrepareWorkerFiles(Source, Worker, privateRuntime);
        Assert.Equal("private-hostfxr", File.ReadAllText(Path.Combine(Worker, ".Updater", "runtimes", "net8-x64", "host", "fxr", "8.0.32", "hostfxr.dll")));
        var start = new System.Diagnostics.ProcessStartInfo { UseShellExecute = false };
        UpdaterBootstrap.ConfigurePrivateRuntime(start, Worker);
        Assert.Equal(Path.Combine(Worker, ".Updater", "runtimes", "net8-x64"), start.Environment["DOTNET_ROOT_X64"]);
    }

    [Fact]
    public void SelfContained_CopiesRuntimeAndWpfClosure()
    {
        CreateSource(true, ["update.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll", "System.Text.Json.dll"],
            ["hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "PresentationNative_cor3.dll"]);
        Write("TerminalSimulation.Wpf.dll");
        Write("config.json");
        Write("MapApp/app.js");
        UpdaterBootstrap.PrepareWorkerFiles(Source, Worker);
        foreach (var name in new[] { "update.exe", "update.dll", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll", "PresentationNative_cor3.dll", "System.Text.Json.dll" })
            Assert.Equal(File.ReadAllText(Path.Combine(Source, name)), File.ReadAllText(Path.Combine(Worker, name)));
        Assert.False(File.Exists(Path.Combine(Worker, "TerminalSimulation.Wpf.dll")));
        Assert.False(Directory.Exists(Path.Combine(Worker, "MapApp")));
    }

    [Fact]
    public void PublishedFlattenedAsset_IsResolvedFromRoot()
    {
        CreateSource(false, ["update.dll", "lib/net8.0/Extra.dll"]);
        File.Move(Path.Combine(Source, "lib/net8.0/Extra.dll"), Path.Combine(Source, "Extra.dll"));
        UpdaterBootstrap.PrepareWorkerFiles(Source, Worker);
        Assert.True(File.Exists(Path.Combine(Worker, "Extra.dll")));
    }

    [Fact]
    public void PublishedNativeSubdirectory_IsPreserved()
    {
        CreateSource(false, ["update.dll"], ["runtimes/win-x64/native/Extra.dll"]);
        UpdaterBootstrap.PrepareWorkerFiles(Source, Worker);
        Assert.True(File.Exists(Path.Combine(Worker, "runtimes/win-x64/native/Extra.dll")));
    }

    [Fact]
    public void MissingRuntimeDependency_FailsBeforeCreatingWorker()
    {
        CreateSource(true, ["update.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll"], ["hostfxr.dll", "hostpolicy.dll", "coreclr.dll"]);
        File.Delete(Path.Combine(Source, "coreclr.dll"));
        Assert.Throws<FileNotFoundException>(() => UpdaterBootstrap.PrepareWorkerFiles(Source, Worker));
        Assert.False(Directory.Exists(Worker));
    }

    [Fact]
    public void SelfContained_IncompleteDepsStillRequiresRuntime()
    {
        CreateSource(true, ["update.dll"]);
        Assert.Throws<FileNotFoundException>(() => UpdaterBootstrap.PrepareWorkerFiles(Source, Worker));
        Assert.False(Directory.Exists(Worker));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("runtimes/../../escape.dll")]
    [InlineData("C:/escape.dll")]
    [InlineData("/escape.dll")]
    [InlineData("..\\escape.dll")]
    public void InvalidAssetPath_IsRejected(string asset)
    {
        CreateSource(false, ["update.dll"], [asset], createAssets: false);
        Assert.Throws<InvalidDataException>(() => UpdaterBootstrap.PrepareWorkerFiles(Source, Worker));
        Assert.False(Directory.Exists(Worker));
    }

    [Fact]
    public void MissingSelectedTarget_IsRejected()
    {
        CreateSource(false, ["update.dll"]);
        File.WriteAllText(Path.Combine(Source, "update.deps.json"), """
            {"runtimeTarget":{"name":"missing"},"targets":{}}
            """);
        Assert.Throws<InvalidDataException>(() => UpdaterBootstrap.PrepareWorkerFiles(Source, Worker));
        Assert.False(Directory.Exists(Worker));
    }

    [Fact]
    public void FrameworkDependent_RidAssetsCopyOnlyCurrentArchitecture()
    {
        CreateSource(false, ["update.dll"]);
        Write("runtimes/win-x64/native/Extra.dll");
        File.WriteAllText(Path.Combine(Source, "update.deps.json"), """
            {"runtimeTarget":{"name":"test"},"targets":{"test":{"update/2.0.0":{
              "runtime":{"update.dll":{}},"runtimeTargets":{
                "runtimes/win-x64/native/Extra.dll":{"rid":"win-x64","assetType":"native"},
                "runtimes/win-x86/native/Extra.dll":{"rid":"win-x86","assetType":"native"}
              }}}}}
            """);
        UpdaterBootstrap.PrepareWorkerFiles(Source, Worker);
        Assert.True(File.Exists(Path.Combine(Worker, "runtimes/win-x64/native/Extra.dll")));
        Assert.False(Directory.Exists(Path.Combine(Worker, "runtimes/win-x86")));
    }

    private void CreateSource(bool selfContained, string[] runtime, string[]? native = null, bool createAssets = true)
    {
        foreach (var name in new[] { "update.exe", "update.dll" }) Write(name);
        File.WriteAllText(Path.Combine(Source, "update.runtimeconfig.json"), selfContained
            ? """{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.28"},{"name":"Microsoft.WindowsDesktop.App","version":"8.0.28"}]}}"""
            : """{"runtimeOptions":{"frameworks":[{"name":"Microsoft.WindowsDesktop.App","version":"8.0.0"}]}}""");
        var groups = new Dictionary<string, object> {
            ["runtime"] = runtime.ToDictionary(name => name, _ => new { }),
            ["native"] = (native ?? []).ToDictionary(name => name, _ => new { })
        };
        File.WriteAllText(Path.Combine(Source, "update.deps.json"), JsonSerializer.Serialize(new {
            runtimeTarget = new { name = "test" },
            targets = new Dictionary<string, object> { ["test"] = new Dictionary<string, object> { ["update/2.0.0"] = groups } }
        }));
        if (createAssets) foreach (var name in runtime.Concat(native ?? [])) Write(name);
    }

    private void Write(string relative)
    {
        var path = Path.Combine(Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
