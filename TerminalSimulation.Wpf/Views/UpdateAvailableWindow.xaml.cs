using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalSimulation.Wpf.Services.Updates;

namespace TerminalSimulation.Wpf.Views;

public partial class UpdateAvailableWindow : Window
{
    private readonly string _currentVersion;
    private readonly string _currentLine;
    private readonly int _currentCompatibilityEpoch;

    internal UpdateCandidate? SelectedCandidate { get; private set; }

    internal UpdateAvailableWindow(
        string currentVersion,
        string currentLine,
        int currentCompatibilityEpoch,
        IReadOnlyList<UpdateCandidate> candidates,
        UpdateCandidate? recommended)
    {
        InitializeComponent();
        _currentVersion = currentVersion;
        _currentLine = currentLine;
        _currentCompatibilityEpoch = currentCompatibilityEpoch;

        CurrentVersionText.Text = currentVersion;
        LineText.Text = $"版本线  {currentLine}";
        CurrentCompatibilityText.Text = $"兼容级别  {currentCompatibilityEpoch}";
        VersionList.ItemsSource = candidates;
        VersionList.SelectedItem = recommended ?? candidates.FirstOrDefault();
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedCandidate = VersionList.SelectedItem as UpdateCandidate;
        if (SelectedCandidate is not { } candidate)
        {
            InstallButton.IsEnabled = false;
            return;
        }

        InstallButton.IsEnabled = true;
        var action = UpdateVersionComparer.Compare(candidate.Version, _currentVersion) > 0 ? "升级" : "回退";
        DirectionText.Text = action;
        InstallButton.Content = $"{action}到所选版本";
        TargetVersionText.Text = candidate.Version;
        TargetMetaText.Text = $"来源：{candidate.SourceName}  ·  版本线：{candidate.Line}  ·  兼容级别：{candidate.CompatibilityEpoch}";
        ReleaseNotesText.Text = candidate.ReleaseNotes;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCandidate is not { } candidate) return;
        if (!string.Equals(candidate.Line, _currentLine, StringComparison.OrdinalIgnoreCase) ||
            candidate.CompatibilityEpoch < _currentCompatibilityEpoch)
        {
            MessageBox.Show("所选版本不属于当前版本线或存在兼容性限制，无法安装。",
                "版本受限", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (UpdateVersionComparer.Compare(candidate.Version, _currentVersion) < 0)
        {
            var result = MessageBox.Show(
                $"确定要从 {_currentVersion} 回退到 {candidate.Version} 吗？\n\n程序文件会被替换，但工作空间和用户数据不会主动删除。",
                "确认回退", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        DialogResult = true;
    }

    private void Later_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
