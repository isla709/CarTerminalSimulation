using System.Windows;

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
        CloseButton.IsEnabled = true;
        CloseButton.Content = result.Success ? "完成" : "关闭";
        if (result.Success)
        {
            CloseButton.Click -= CloseButton_Click;
            CloseButton.Click += (_, _) => Application.Current.Shutdown();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown(1);
}
