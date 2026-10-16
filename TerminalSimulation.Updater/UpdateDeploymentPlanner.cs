using System.Text.Json;

namespace TerminalSimulation.Updater;

internal static class UpdateDeploymentPlanner
{
    internal static IReadOnlyList<string> GetObsoleteRuntimeFiles(string installation, string payload, string mainExecutable)
    {
        var relativeExecutable = Path.GetRelativePath(installation, mainExecutable);
        if (Path.IsPathRooted(relativeExecutable) || relativeExecutable.Split(Path.DirectorySeparatorChar).Contains(".."))
            throw new InvalidDataException("主程序不在安装目录内。");
        var runtimeConfig = Path.ChangeExtension(relativeExecutable, ".runtimeconfig.json");
        var newConfig = Path.Combine(payload, runtimeConfig);
        if (!File.Exists(newConfig)) return [];
        using var config = JsonDocument.Parse(File.ReadAllText(newConfig));
        var options = config.RootElement.GetProperty("runtimeOptions");
        var frameworkDependent = options.TryGetProperty("framework", out _) ||
            (options.TryGetProperty("frameworks", out var frameworks) && frameworks.GetArrayLength() > 0);
        if (!frameworkDependent || options.TryGetProperty("includedFrameworks", out _)) return [];

        var obsolete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appDirectory = Path.GetDirectoryName(relativeExecutable) ?? "";
        // These app-local host files redirect framework lookup into the old
        // self-contained installation. Also heal a previous mixed deployment.
        AddIfObsolete("hostfxr.dll");
        AddIfObsolete("hostpolicy.dll");
        var oldDeps = Path.Combine(installation, Path.ChangeExtension(relativeExecutable, ".deps.json"));
        if (File.Exists(oldDeps))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(oldDeps));
            var root = document.RootElement;
            var targetName = root.GetProperty("runtimeTarget").GetProperty("name").GetString();
            if (targetName is not null && root.GetProperty("targets").TryGetProperty(targetName, out var target))
            {
                foreach (var library in target.EnumerateObject())
                {
                    if (!library.Name.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.", StringComparison.Ordinal) &&
                        !library.Name.StartsWith("runtimepack.Microsoft.WindowsDesktop.App.Runtime.", StringComparison.Ordinal)) continue;
                    foreach (var group in new[] { "runtime", "native" })
                    {
                        if (!library.Value.TryGetProperty(group, out var assets)) continue;
                        foreach (var asset in assets.EnumerateObject())
                        {
                            var path = asset.Name.Replace('\\', '/');
                            if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Any(p => p is "" or "." or ".."))
                                throw new InvalidDataException($"旧运行时清单包含无效路径：{asset.Name}");
                            // Runtime packs publish flat; never infer deletion of
                            // arbitrary data/directories from dependency metadata.
                            var name = Path.GetFileName(path);
                            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name == "createdump.exe")
                                AddIfObsolete(name);
                        }
                    }
                }
            }
        }
        return obsolete.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        void AddIfObsolete(string name)
        {
            var relative = Path.Combine(appDirectory, name);
            if (File.Exists(Path.Combine(installation, relative)) && !File.Exists(Path.Combine(payload, relative)))
                obsolete.Add(relative);
        }
    }
}
