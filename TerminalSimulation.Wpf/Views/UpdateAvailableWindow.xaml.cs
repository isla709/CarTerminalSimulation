using System.Windows;
using System.Windows.Input;
using TerminalSimulation.Wpf.Services.Updates;

namespace TerminalSimulation.Wpf.Views;

public partial class UpdateAvailableWindow : Window
{
    internal UpdateAvailableWindow(string currentVersion, UpdateCandidate candidate)
    {
        InitializeComponent();
        CurrentVersionText.Text = currentVersion;
        NewVersionText.Text = candidate.Version;
        SourceText.Text = $"来源：{candidate.SourceName}";
        ReleaseNotesText.Text = candidate.ReleaseNotes;
    }

    private void Install_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Later_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
