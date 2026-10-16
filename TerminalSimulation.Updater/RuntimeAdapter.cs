using System.Text.Json;

namespace TerminalSimulation.Updater;

internal sealed record RuntimeSelection(string Description, string? DotnetRoot = null, string? Hostfxr = null);

internal static class RuntimeAdapter
{
    internal static RuntimeSelection Preflight(UpdatePlan plan, string payload)
    {
        var name = Path.GetFileNameWithoutExtension(plan.MainExecutablePath);
        foreach (var extension in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
            if (!File.Exists(Path.Combine(payload, name + extension))) throw new InvalidDataException($"目标程序缺少 {name + extension}");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(payload, name + ".deps.json")));
        var target = deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString() ?? "";
        if (target.Contains("win-", StringComparison.OrdinalIgnoreCase) && !target.EndsWith("win-x64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新包架构不兼容：本更新器仅支持 Windows x64。");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(payload, name + ".runtimeconfig.json")));
        var options = json.RootElement.GetProperty("runtimeOptions");
        if (options.TryGetProperty("includedFrameworks", out var included))
        {
            var required = new List<string> { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll" };
            if (included.EnumerateArray().Any(f => f.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App")) required.Add("PresentationFramework.dll");
            foreach (var file in required)
                if (!File.Exists(Path.Combine(payload, file))) throw new InvalidDataException($"自包含目标缺少运行时文件 {file}，尚未修改安装目录。");
            return new RuntimeSelection("目标自包含运行时 · Windows x64");
        }
        var frameworks = new List<(string Name, Version Version)>();
        if (options.TryGetProperty("framework", out var framework)) AddFramework(framework);
        if (options.TryGetProperty("frameworks", out var many)) foreach (var item in many.EnumerateArray()) AddFramework(item);
        if (frameworks.Count == 0) throw new InvalidDataException("目标运行时声明无效。");
        var privateRoot = Path.Combine(UpdateKernel.Root(plan.TargetDirectory), "runtimes", $"net{frameworks.Max(f => f.Version.Major)}-x64");
        var privateHost = FindCompatible(privateRoot, frameworks, options);
        if (privateHost is not null && Probe(privateRoot, privateHost))
            return new RuntimeSelection("使用随包私有 .NET 运行时", privateRoot, privateHost);
        var systemRoots = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
            Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\dotnet\Setup\InstalledVersions\x64", "InstallLocation", null) as string
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        // Keep framework-dependent apps runnable even if a system runtime is removed later.
        // The process-scoped QA switch still verifies that this private path is self-sufficient.
        if (Environment.GetEnvironmentVariable("CTS_RUNTIME_PRIVATE_ONLY") != "1")
            foreach (var root in systemRoots)
                if (FindCompatible(root!, frameworks, options) is { } fxr && Probe(root!, fxr)) return new RuntimeSelection("使用兼容的系统 .NET 运行时", root, fxr);
        throw new InvalidDataException("随包私有缓存和系统中都没有目标所需的兼容运行时；请使用包含该运行时的新版完整发布包。安装目录尚未修改。");

        void AddFramework(JsonElement element) => frameworks.Add((element.GetProperty("name").GetString()!, Version.Parse(element.GetProperty("version").GetString()!)));
        bool Probe(string root, string fxr)
        {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(UpdateKernel.Root(plan.TargetDirectory), "compat-host.exe")) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            foreach (var argument in new[] { "--probe", Path.Combine(payload, name + ".dll"), root, fxr }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("无法启动运行时预检。");
            if (!process.WaitForExit(15000)) { process.Kill(true); process.WaitForExit(); return false; }
            return process.ExitCode == 0;
        }
    }

    internal static string? FindCompatible(string root, IReadOnlyList<(string Name, Version Version)> frameworks, JsonElement options)
    {
        var exact = options.TryGetProperty("rollForward", out var roll) && roll.GetString()?.Equals("Disable", StringComparison.OrdinalIgnoreCase) == true;
        foreach (var (name, requested) in frameworks)
        {
            if (name is not ("Microsoft.NETCore.App" or "Microsoft.WindowsDesktop.App")) return null;
            var directory = Path.Combine(root, "shared", name);
            if (!Directory.Exists(directory) || !Directory.EnumerateDirectories(directory).Any(d =>
                    Version.TryParse(Path.GetFileName(d), out var available) &&
                    (exact ? available == requested : available.Major == requested.Major && available.Minor == requested.Minor && available >= requested))) return null;
        }
        var hosts = Path.Combine(root, "host", "fxr");
        return Directory.Exists(hosts) ? Directory.EnumerateDirectories(hosts)
            .Where(d => Version.TryParse(Path.GetFileName(d), out var version) && version.Major == frameworks.Max(f => f.Version.Major))
            .OrderByDescending(d => Version.Parse(Path.GetFileName(d))).Select(d => Path.Combine(d, "hostfxr.dll")).FirstOrDefault(File.Exists) : null;
    }

    internal static void ApplyHost(UpdatePlan plan, RuntimeSelection selection, FileTransaction transaction)
    {
        if (selection.DotnetRoot is null) return;
        var component = UpdateKernel.Root(plan.TargetDirectory);
        var host = Path.Combine(component, "compat-host.exe");
        if (!File.Exists(host)) throw new FileNotFoundException("持久启动兼容器缺失。", host);
        transaction.Replace(host, Path.GetRelativePath(plan.TargetDirectory, plan.MainExecutablePath));
        transaction.Write(".Updater/app-runtime.txt", selection.DotnetRoot);
        transaction.Write(".Updater/app-hostfxr.txt", selection.Hostfxr!);
    }
}
