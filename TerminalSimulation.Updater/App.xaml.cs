using System.Windows;

namespace TerminalSimulation.Updater;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var options = UpdaterArguments.Parse(e.Args);
            if (!options.IsWorker)
            {
                UpdaterBootstrap.LaunchWorker(options.PlanPath);
                Shutdown();
                return;
            }

            var window = new UpdaterWindow();
            MainWindow = window;
            window.Show();

            var progress = new Progress<UpdateProgress>(window.ReportProgress);
            var result = await new UpdateEngine().RunAsync(options.PlanPath, progress, CancellationToken.None);
            window.ReportCompleted(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "更新程序", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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
