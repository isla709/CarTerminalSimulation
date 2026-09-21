using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TerminalSimulation.Wpf.Services.Updates;

internal static class UpdateLauncher
{
    public static string Start(
        UpdateCandidate candidate,
        string currentLine,
        int currentCompatibilityEpoch)
    {
        if (!string.Equals(candidate.Line, currentLine, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"不能从版本线 {currentLine} 切换到 {candidate.Line}。");
        if (candidate.CompatibilityEpoch < currentCompatibilityEpoch)
            throw new InvalidOperationException(
                $"目标版本兼容级别 {candidate.CompatibilityEpoch} 低于当前级别 {currentCompatibilityEpoch}，不允许回退。");

        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定当前主程序路径。");
        var updaterPath = Path.Combine(AppContext.BaseDirectory, "update.exe");
        foreach (var fileName in new[] { "update.exe", "update.dll", "update.deps.json", "update.runtimeconfig.json" })
        {
            var requiredPath = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(requiredPath))
                throw new FileNotFoundException($"更新组件不完整，缺少 {fileName}。请重新安装完整发布包。", requiredPath);
        }

        var planDirectory = Path.Combine(
            Path.GetTempPath(),
            "CarTerminalSimulation",
            "Updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(planDirectory);
        var planPath = Path.Combine(planDirectory, "update-plan.json");
        var plan = new UpdatePlanDocument
        {
            SchemaVersion = 2,
            Version = candidate.Version,
            CurrentLine = currentLine,
            TargetLine = candidate.Line,
            CurrentCompatibilityEpoch = currentCompatibilityEpoch,
            TargetCompatibilityEpoch = candidate.CompatibilityEpoch,
            PackageUrl = candidate.PackageUri.AbsoluteUri,
            Sha256 = candidate.Sha256,
            PackageSize = candidate.PackageSize,
            TargetDirectory = Path.GetFullPath(AppContext.BaseDirectory),
            MainExecutablePath = Path.GetFullPath(executablePath),
            ParentProcessId = Environment.ProcessId,
            AllowInsecureHttp = candidate.AllowInsecureHttp,
            Delete = candidate.Delete.ToList()
        };
        // Keep PascalCase property names for compatibility with Preview6's updater.
        // New updaters accept both naming styles, allowing an already-installed
        // Preview6 build to update successfully as well.
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--plan");
        startInfo.ArgumentList.Add(planPath);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 update.exe。");
        return planPath;
    }

    private sealed class UpdatePlanDocument
    {
        public int SchemaVersion { get; set; } = 2;
        public string Version { get; set; } = string.Empty;
        public string CurrentLine { get; set; } = string.Empty;
        public string TargetLine { get; set; } = string.Empty;
        public int CurrentCompatibilityEpoch { get; set; }
        public int TargetCompatibilityEpoch { get; set; }
        public string PackageUrl { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long? PackageSize { get; set; }
        public string TargetDirectory { get; set; } = string.Empty;
        public string MainExecutablePath { get; set; } = string.Empty;
        public int ParentProcessId { get; set; }
        public bool AllowInsecureHttp { get; set; }
        public List<string> Delete { get; set; } = [];
    }
}
