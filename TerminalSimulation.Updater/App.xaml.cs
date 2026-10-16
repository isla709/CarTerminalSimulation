using System.Windows;

namespace TerminalSimulation.Updater;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        TerminalSimulation.Shared.WindowsAppIdentity.Initialize("CarTerminalSimulation.Updater");
        base.OnStartup(e);

        if (e.Args.Contains("--build-setup"))
        {
            try { SetupPackage.Create(Argument(e.Args, "--input"), Argument(e.Args, "--stub"), Argument(e.Args, "--output"),
                Argument(e.Args, "--version"), Argument(e.Args, "--line"), int.Parse(Argument(e.Args, "--epoch"))); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(Argument(e.Args, "--output") + ".error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--prepare-install-target"))
        {
            try
            {
                InstallationService.PrepareProgramFilesTarget(Argument(e.Args, "--target"), Argument(e.Args, "--user-sid"));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "安装程序", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--setup"))
        {
            try
            {
                var package = Argument(e.Args, "--package");
                if (e.Args.Contains("--update-stage"))
                {
                    var output = Argument(e.Args, "--output");
                    if (Path.GetFileName(Path.GetFullPath(output)) != "staging") throw new ArgumentException("更新载荷目录名必须为 staging。");
                    await Task.Run(() => SetupPackage.Extract(package, output, Argument(e.Args, "--expected-version")));
                    Shutdown(0); return;
                }
                MainWindow = new InstallerWindow(package); MainWindow.Show();
            }
            catch (Exception ex) { if (!e.Args.Contains("--update-stage")) MessageBox.Show(ex.Message, "安装程序", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--uninstall"))
        {
            try
            {
                var target = Argument(e.Args, "--target");
                if (!e.Args.Contains("--installer-worker"))
                {
                    var worker = Path.Combine(Path.GetTempPath(), "CarTerminalSimulation", "Uninstall", Guid.NewGuid().ToString("N"));
                    UpdaterBootstrap.PrepareWorkerFiles(AppContext.BaseDirectory, worker);
                    var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(worker, "update.exe")) { UseShellExecute = false, WorkingDirectory = worker };
                    UpdaterBootstrap.ConfigurePrivateRuntime(info, worker);
                    foreach (var value in new[] { "--uninstall", "--target", target, "--installer-worker" }) info.ArgumentList.Add(value);
                    _ = System.Diagnostics.Process.Start(info) ?? throw new IOException("无法启动独立卸载程序。");
                    Shutdown(); return;
                }
                MainWindow = new InstallerWindow(null, target); MainWindow.Show(); AcknowledgeReady();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "卸载程序", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
            return;
        }

        if (e.Args.Contains("--health-check", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _ = System.Text.Json.JsonSerializer.Serialize(new UpdatePlan(), UpdaterJsonContext.Default.UpdatePlan);
                _ = new UpdaterWindow();
                Shutdown(0);
            }
            catch { Shutdown(1); }
            return;
        }

        UpdaterArguments? options = null;
        try
        {
            options = UpdaterArguments.Parse(e.Args);
            if (!options.IsWorker)
            {
                UpdaterBootstrap.LaunchWorker(options.PlanPath);
                Shutdown();
                return;
            }

            var window = new UpdaterWindow();
            if (Environment.GetEnvironmentVariable("CTS_QA_WINDOW_HIDDEN") == "1")
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -30000;
                window.ShowActivated = false;
            }
            MainWindow = window;
            window.Show();
            AcknowledgeReady();

            var progress = new Progress<UpdateProgress>(window.ReportProgress);
            var result = await Task.Run(() => new UpdateEngine().RunAsync(options.PlanPath, progress, window.CancellationToken));
            window.ReportCompleted(result);
        }
        catch (Exception ex)
        {
            // Bootstrap errors happen before UpdateEngine can create update.log.
            // Preserve diagnostics beside the plan, not in the install directory.
            if (options is not null)
            {
                try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(options.PlanPath)!, "update-bootstrap.log"), ex.ToString()); }
                catch { /* Reporting must not hide the original startup error. */ }
            }
            MessageBox.Show(ex.Message, "更新程序", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string Argument(IReadOnlyList<string> args, string name)
    {
        var index = Array.IndexOf(args.ToArray(), name);
        if (index < 0 || index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1])) throw new ArgumentException("缺少参数 " + name);
        return args[index + 1];
    }
    private static void AcknowledgeReady()
    {
        if (Environment.GetEnvironmentVariable("CTS_UPDATER_READY") is not { Length: > 0 } ready) return;
        var path = Path.GetFullPath(ready);
        if (path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith("CTS-updater-ready-", StringComparison.Ordinal))
            File.WriteAllText(path, "ready");
    }
}

internal sealed record UpdaterArguments(string PlanPath, bool IsWorker)
{
    public static UpdaterArguments Parse(IReadOnlyList<string> args)
    {
        var worker = args.Any(arg => string.Equals(arg, "--worker", StringComparison.OrdinalIgnoreCase));
        var planIndex = Array.FindIndex(args.ToArray(), arg => string.Equals(arg, "--plan", StringComparison.OrdinalIgnoreCase));
        if (planIndex < 0 || planIndex + 1 >= args.Count || string.IsNullOrWhiteSpace(args[planIndex + 1]))
            throw new ArgumentException("缺少更新计划参数 --plan。");

        return new UpdaterArguments(Path.GetFullPath(args[planIndex + 1]), worker);
    }
}
