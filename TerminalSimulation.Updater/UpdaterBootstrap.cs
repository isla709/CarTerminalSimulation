using System.Diagnostics;
using System.Text.Json;

namespace TerminalSimulation.Updater;

internal static class UpdaterBootstrap
{
    public static void LaunchWorker(string planPath)
    {
        if (!File.Exists(planPath)) throw new FileNotFoundException("更新计划不存在。", planPath);

        var sourceDirectory = AppContext.BaseDirectory;
        var workerDirectory = Path.Combine(Path.GetTempPath(), "CarTerminalSimulation", "Updater", Guid.NewGuid().ToString("N"));
        PrepareWorkerFiles(sourceDirectory, workerDirectory);

        var worker = Path.Combine(workerDirectory, "update.exe");
        var startInfo = new ProcessStartInfo(worker)
        {
            UseShellExecute = false,
            WorkingDirectory = workerDirectory
        };
        ConfigurePrivateRuntime(startInfo, workerDirectory);
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--plan");
        startInfo.ArgumentList.Add(planPath);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新工作进程。");
    }

    internal static void PrepareWorkerFiles(string sourceDirectory, string workerDirectory, string? privateRuntimeDirectory = null)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        workerDirectory = Path.GetFullPath(workerDirectory);
        if (string.Equals(sourceDirectory, workerDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("更新工作目录不能与安装目录相同。", nameof(workerDirectory));

        // Resolve and validate the complete closure before writing anything.
        // Self-contained workers need their own runtime after the main app exits;
        // four update.* files alone cannot run from the temporary directory.
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "update.exe", "update.dll", "update.deps.json", "update.runtimeconfig.json" })
            AddAsset(name);

        using var dependencies = JsonDocument.Parse(File.ReadAllText(files["update.deps.json"]));
        var targetName = dependencies.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(targetName) ||
            !dependencies.RootElement.GetProperty("targets").TryGetProperty(targetName, out var target))
            throw new InvalidDataException("更新程序依赖清单缺少当前运行时目标。");
        foreach (var library in target.EnumerateObject())
        {
            foreach (var group in new[] { "runtime", "native" })
            {
                if (!library.Value.TryGetProperty(group, out var assets)) continue;
                foreach (var asset in assets.EnumerateObject())
                    if (Path.GetFileName(asset.Name) != "_._") AddAsset(asset.Name);
            }
            // Framework-dependent builds can also have RID-specific libraries.
            if (library.Value.TryGetProperty("runtimeTargets", out var runtimeTargets))
            {
                foreach (var asset in runtimeTargets.EnumerateObject())
                {
                    var kind = asset.Value.GetProperty("assetType").GetString();
                    var rid = asset.Value.GetProperty("rid").GetString();
                    if (kind is "runtime" or "native" &&
                        string.Equals(rid, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(asset.Name) != "_._") AddAsset(asset.Name);
                }
            }
        }

        using var configuration = JsonDocument.Parse(File.ReadAllText(files["update.runtimeconfig.json"]));
        if (configuration.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out var frameworks))
        {
            foreach (var name in new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll" })
                AddAsset(name);
            if (frameworks.EnumerateArray().Any(f => f.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App"))
                AddAsset("PresentationFramework.dll");
        }
        else
        {
            // Framework-dependent updater kernels use the installation's private runtime.
            // Copy it beside the detached worker so the transaction can replace the installed
            // runtime without holding any of its files open.
            var privateRuntime = Path.GetFullPath(privateRuntimeDirectory ?? Path.Combine(sourceDirectory, "..", "..", "runtimes", "net8-x64"));
            if (Directory.Exists(privateRuntime)) foreach (var file in Directory.GetFiles(privateRuntime, "*", SearchOption.AllDirectories))
            {
                var relative = ".Updater/runtimes/net8-x64/" + Path.GetRelativePath(privateRuntime, file).Replace('\\', '/');
                files.TryAdd(relative, file);
            }
        }

        Directory.CreateDirectory(workerDirectory);
        foreach (var (relativePath, sourcePath) in files)
        {
            var destination = Path.Combine(workerDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: true);
        }

        void AddAsset(string assetPath)
        {
            var relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
                relativePath.Split(Path.DirectorySeparatorChar).Any(segment => segment is "" or "." or ".."))
                throw new InvalidDataException($"更新依赖路径无效：{assetPath}");
            var source = Path.Combine(sourceDirectory, relativePath);
            // Publish usually flattens framework and managed package assemblies.
            // Preserve an actual subdirectory when the file is published there.
            if (!File.Exists(source))
            {
                relativePath = Path.GetFileName(relativePath);
                source = Path.Combine(sourceDirectory, relativePath);
            }
            if (!File.Exists(source))
                throw new FileNotFoundException($"更新程序依赖缺失：{assetPath}。请重新解压完整发布包。", source);
            files.TryAdd(relativePath, source);
        }
    }

    internal static void ConfigurePrivateRuntime(ProcessStartInfo startInfo, string workerDirectory)
    {
        var runtime = Path.Combine(workerDirectory, ".Updater", "runtimes", "net8-x64");
        if (!Directory.Exists(runtime)) return; // Self-contained legacy updater bundles need no runtime root.
        startInfo.Environment["DOTNET_ROOT"] = runtime;
        startInfo.Environment["DOTNET_ROOT_X64"] = runtime;
        startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
    }
}
