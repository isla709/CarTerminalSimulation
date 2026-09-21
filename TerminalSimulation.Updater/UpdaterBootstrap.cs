using System.Diagnostics;

namespace TerminalSimulation.Updater;

internal static class UpdaterBootstrap
{
    public static void LaunchWorker(string planPath)
    {
        if (!File.Exists(planPath)) throw new FileNotFoundException("更新计划不存在。", planPath);

        var sourceDirectory = AppContext.BaseDirectory;
        var workerDirectory = Path.Combine(Path.GetTempPath(), "CarTerminalSimulation", "Updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerDirectory);

        foreach (var name in new[] { "update.exe", "update.dll", "update.deps.json", "update.runtimeconfig.json" })
        {
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source))
                throw new FileNotFoundException($"更新程序组件缺失：{name}", source);
            File.Copy(source, Path.Combine(workerDirectory, name), overwrite: true);
        }

        var worker = Path.Combine(workerDirectory, "update.exe");
        var startInfo = new ProcessStartInfo(worker)
        {
            UseShellExecute = false,
            WorkingDirectory = workerDirectory
        };
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--plan");
        startInfo.ArgumentList.Add(planPath);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动更新工作进程。");
    }
}
