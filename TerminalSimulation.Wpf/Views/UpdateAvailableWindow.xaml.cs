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
        // Keep upgrades at the top of the list and select the newest upgrade by default.
        // Same-order builds (for example a local random suffix versus a numbered Release)
        // are intentionally presented as a switch, not as a downgrade.
        var orderedCandidates = candidates
            .OrderByDescending(candidate => UpdateVersionComparer.Compare(candidate.Version, currentVersion) > 0)
            .ThenByDescending(candidate => UpdateVersionComparer.Compare(candidate.Version, currentVersion))
            .ThenByDescending(candidate => candidate.PublishedAt)
            .ToList();
        VersionList.ItemsSource = orderedCandidates;
        VersionList.SelectedItem = recommended ?? orderedCandidates.FirstOrDefault();
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
        var comparison = UpdateVersionComparer.Compare(candidate.Version, _currentVersion);
        var action = comparison > 0 ? "升级" : comparison < 0 ? "回退" : "切换";
        DirectionText.Text = action;
        InstallButton.Content = $"{action}到所选版本";
        InstallButton.Background = action switch
        {
            "升级" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
            "切换" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 150, 136)),
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 141, 34))
        };
        InstallButton.BorderBrush = InstallButton.Background;
        InstallButton.Foreground = System.Windows.Media.Brushes.White;
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
