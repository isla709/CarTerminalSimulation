using System.Windows;
using System.Windows.Threading;

namespace TerminalSimulation.Updater;

public partial class UpdaterWindow : Window
{
    public UpdaterWindow() => InitializeComponent();

    public void ReportProgress(UpdateProgress progress)
    {
        VersionText.Text = string.IsNullOrWhiteSpace(progress.Version) ? string.Empty : $"目标版本：{progress.Version}";
        StatusText.Text = progress.Message;
        ProgressBar.IsIndeterminate = progress.Percent is null;
        if (progress.Percent is { } percent) ProgressBar.Value = percent;
    }

    public void ReportCompleted(UpdateResult result)
    {
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = result.Success ? 100 : 0;
        StatusText.Text = result.Message;
        CloseButton.IsEnabled = !result.Success;
        CloseButton.Content = result.Success ? "正在启动…" : "关闭";
        if (result.Success)
        {
            // UpdateEngine has already started the new main process. Keep the
            // completion state visible briefly, then close without user input.
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                Application.Current.Shutdown();
            };
            closeTimer.Start();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown(1);
}
