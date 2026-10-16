using System.Security.Cryptography;
using System.Text.Json;

namespace TerminalSimulation.Updater;

/// <summary>The compatibility protocol remains v2; the persistent engine evolves independently.</summary>
internal static class UpdateKernel
{
    internal static readonly Version EngineVersion = new(typeof(UpdateKernel).Assembly.GetName().Version!.ToString(3));
    internal static string Root(string installation) => Path.Combine(installation, ".Updater");
    internal static bool IsProtected(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var first = normalized.Split('/')[0];
        return first.Equals(".Updater", StringComparison.OrdinalIgnoreCase) ||
               first.Equals(".Installation", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("Workspaces", StringComparison.OrdinalIgnoreCase) ||
               first.Equals("Plugins", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("config.json", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("utility_settings.json", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("update-sources.json", StringComparison.OrdinalIgnoreCase) ||
               new[] { "update.exe", "update.dll", "update.deps.json", "update.runtimeconfig.json", "uninstall.exe" }
                   .Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Replace('\\', '/').Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException($"组件路径不安全：{relative}");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件路径越界。");
        var current = path;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (!current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"更新路径包含重解析链接：{relative}");
            current = Path.GetDirectoryName(current)!;
        }
        return path;
    }

    internal static KernelState ReadState(string installation)
    {
        var path = Path.Combine(Root(installation), "state.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), UpdaterJsonContext.Default.KernelState)
              ?? throw new InvalidDataException("更新内核状态无效，请勿手动删除兼容信息。")
            : new KernelState();
    }

    internal static void ValidateState(UpdatePlan plan, KernelState state)
    {
        if (state.Line.Length > 0 && !state.Line.Equals(plan.TargetLine, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("持久更新内核禁止跨版本线安装。");
        if (plan.TargetCompatibilityEpoch < state.HighestCompatibilityEpoch)
            throw new InvalidDataException($"本机已使用兼容级别 {state.HighestCompatibilityEpoch}，旧主程序不能绕过回退保护。");
    }

    internal static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".new-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    internal static BundleManifest? ValidateBundle(string directory, string? activeEngine = null)
    {
        var root = Root(directory);
        var path = Path.Combine(root, "bundle.json");
        if (!File.Exists(path)) return null;
        var bundle = JsonSerializer.Deserialize(File.ReadAllText(path), UpdaterJsonContext.Default.BundleManifest)
                     ?? throw new InvalidDataException("更新内核清单无效。");
        if (bundle.SchemaVersion != 1 || !Version.TryParse(bundle.KernelVersion, out _) ||
            !bundle.Engine.StartsWith("engines/", StringComparison.Ordinal) || bundle.Files.Count < 5)
            throw new InvalidDataException("更新内核清单格式不受支持。");
        if (bundle.EntrySha256.Length > 0)
        {
            var entry = SafePath(directory, "update.exe");
            if (bundle.EntrySha256.Length != 64 || !File.Exists(entry) ||
                !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry))).Equals(bundle.EntrySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("原生更新入口校验失败。");
        }
        foreach (var (relative, expected) in bundle.Files)
        {
            if (relative != "compat-host.exe" && !relative.StartsWith("engines/", StringComparison.Ordinal) && !relative.StartsWith("runtimes/", StringComparison.Ordinal))
                throw new InvalidDataException($"组件清单包含不允许的可变文件：{relative}");
            if (activeEngine is not null && relative.StartsWith("engines/", StringComparison.Ordinal) && !relative.StartsWith(activeEngine + "/", StringComparison.Ordinal)) continue;
            var file = SafePath(root, relative);
            if (expected.Length != 64 || !File.Exists(file) ||
                !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新兼容组件校验失败：{relative}");
        }
        foreach (var required in new[] { bundle.Engine + "/update.exe", bundle.Engine + "/update.dll", "compat-host.exe" })
            if (!bundle.Files.ContainsKey(required)) throw new InvalidDataException($"组件清单缺少 {required}");
        return bundle;
    }

    internal static BundleManifest MergeBundle(BundleManifest retained, BundleManifest incoming)
    {
        var merged = new BundleManifest { SchemaVersion = incoming.SchemaVersion, KernelVersion = incoming.KernelVersion,
            Engine = incoming.Engine, RuntimeVersion = incoming.RuntimeVersion, EntrySha256 = incoming.EntrySha256,
            Files = new(retained.Files, StringComparer.OrdinalIgnoreCase) };
        foreach (var (file, hash) in incoming.Files) merged.Files[file] = hash;
        return merged;
    }

    // New immutable slots are installed only from the already SHA-256 verified
    // application package. Older payloads never write the protected component tree.
    internal static void StageNewEngine(string installation, string payload, BundleManifest? bundle)
    {
        if (bundle is null) return;
        var destinationRoot = Root(installation);
        var engine = SafePath(destinationRoot, bundle.Engine);
        if (!Directory.Exists(engine))
        {
            var staging = engine + ".new-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            foreach (var relative in bundle.Files.Keys.Where(p => p.StartsWith(bundle.Engine + "/", StringComparison.Ordinal)))
            {
                var destination = SafePath(staging, relative[(bundle.Engine.Length + 1)..]);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(SafePath(Root(payload), relative), destination, false);
            }
            // Windows can briefly deny directory renames while a newly copied
            // runtime is being inspected. Retry the same atomic rename only;
            // never fall back to exposing a partially copied active slot.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Move(staging, engine); break; }
                catch (IOException) when (attempt < 20) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(250); }
            }
        }
        foreach (var (relative, expected) in bundle.Files.Where(p => p.Key.StartsWith(bundle.Engine + "/", StringComparison.Ordinal)))
            if (!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SafePath(destinationRoot, relative)))).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("已暂存的新更新内核校验失败。");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(engine, "update.exe")) { UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--health-check");
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("无法预检新更新内核。");
        if (!process.WaitForExit(20000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("新更新内核自检超时，未激活。"); }
        if (process.ExitCode != 0) throw new InvalidDataException("新更新内核自检失败，保留原内核。");
        // Activation is finalized after successful application startup. The old
        // slot remains available to the native launcher's readiness fallback.
    }

    internal static void StageRuntimeCache(string installation, string payload, BundleManifest? bundle)
    {
        if (bundle is null) return;
        foreach (var relative in bundle.Files.Keys.Where(p => p.StartsWith("runtimes/", StringComparison.Ordinal)))
        {
            var destination = SafePath(Root(installation), relative);
            if (File.Exists(destination))
            {
                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination)));
                if (!actual.Equals(bundle.Files[relative], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"私有运行时缓存同路径内容冲突：{relative}。请使用独立版本目录。");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".new-" + Guid.NewGuid().ToString("N");
            File.Copy(SafePath(Root(payload), relative), temporary);
            File.Move(temporary, destination);
        }
    }

    internal static void Commit(UpdatePlan plan, KernelState state, BundleManifest? bundle)
    {
        state.Line = plan.TargetLine;
        state.HighestCompatibilityEpoch = Math.Max(state.HighestCompatibilityEpoch, plan.TargetCompatibilityEpoch);
        state.ApplicationVersion = plan.Version;
        var previousKernel = Version.TryParse(state.KernelVersion, out var recorded) ? recorded : EngineVersion;
        state.KernelVersion = (previousKernel > EngineVersion ? previousKernel : EngineVersion).ToString();
        state.PendingTransaction = "";
        var root = Root(plan.TargetDirectory);
        if (bundle is not null && Version.Parse(bundle.KernelVersion) > previousKernel && Version.Parse(bundle.KernelVersion) > EngineVersion)
        {
            var pointer = Path.Combine(root, "current.txt");
            if (File.Exists(pointer)) AtomicWrite(Path.Combine(root, "last-good.txt"), File.ReadAllText(pointer));
            AtomicWrite(pointer, bundle.Engine);
            state.KernelVersion = bundle.KernelVersion;
        }
        AtomicWrite(Path.Combine(root, "state.json"), JsonSerializer.Serialize(state, UpdaterJsonContext.Default.KernelState));
    }
}

internal sealed class KernelState
{
    public string Line { get; set; } = "";
    public int HighestCompatibilityEpoch { get; set; }
    public string ApplicationVersion { get; set; } = "";
    public string KernelVersion { get; set; } = UpdateKernel.EngineVersion.ToString();
    public string PendingTransaction { get; set; } = "";
    public List<string> OwnedFiles { get; set; } = [];
}

internal sealed class BundleManifest
{
    public int SchemaVersion { get; set; }
    public string KernelVersion { get; set; } = "";
    public string Engine { get; set; } = "";
    public string RuntimeVersion { get; set; } = "";
    public string EntrySha256 { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
