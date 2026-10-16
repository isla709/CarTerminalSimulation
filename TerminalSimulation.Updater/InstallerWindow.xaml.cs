using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Animation;

namespace TerminalSimulation.Updater;

public partial class InstallerWindow : Window
{
    private readonly string? _package;
    private readonly bool _uninstall;
    private CancellationTokenSource _cancel = new();
    private bool _busy, _finished;
    public InstallerWindow(string? package, string? target = null)
    {
        InitializeComponent(); _package = package; _uninstall = package is null;
        DirectoryInput.Text = target ?? InstallationService.PreferredDirectory;
        if (_uninstall)
        {
            Title = "卸载 · 车载定位终端模拟系统"; Heading.Text = "卸载车载定位终端模拟系统";
            ActionTitle.Text = "卸载程序"; DataNotice.Text = "配置、插件和工作区会保留。";
            InstallButton.Content = "确认卸载"; Shortcuts.Visibility = Visibility.Collapsed;
            DirectoryInput.IsReadOnly = true;
            VersionText.Text = InstallationService.Read(DirectoryInput.Text)?.Version;
        }
        else
        {
            VersionText.Text = SetupPackage.Read(package!).Version;
            InstallationRecord? existing = null;
            try { existing = Directory.Exists(DirectoryInput.Text) ? InstallationService.Read(DirectoryInput.Text) : null; }
            catch (Exception) { Status.Text = "安装记录损坏，可选择原目录重新安装以修复登记。"; }
            if (existing is not null) { Desktop.IsChecked = existing.DesktopShortcut; StartMenu.IsChecked = existing.StartMenuShortcut; }
        }
        Loaded += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation && SystemParameters.UIEffects)
                LayoutRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        };
    }
    private void Browse(object sender, RoutedEventArgs e)
    {
        if (_uninstall) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择专用安装目录", InitialDirectory = Path.GetDirectoryName(DirectoryInput.Text) };
        if (dialog.ShowDialog(this) == true) DirectoryInput.Text = dialog.FolderName;
    }
    private async void Install(object sender, RoutedEventArgs e)
    {
        if (_finished) { Close(); return; }
        if (_cancel.IsCancellationRequested) { _cancel.Dispose(); _cancel = new(); }
        Progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null); Progress.Value = 0;
        var target = DirectoryInput.Text;
        var desktop = Desktop.IsChecked == true; var startMenu = StartMenu.IsChecked == true;
        _busy = true; DirectoryPanel.IsEnabled = Shortcuts.IsEnabled = InstallButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = true;
        var progress = new Progress<UpdateProgress>(value =>
        {
            Status.Text = value.Message;
            if (value.Percent is { } percent)
            {
                Progress.IsIndeterminate = false;
                var next = Math.Max(Progress.Value, Math.Clamp(percent, 0, 100));
                var old = Progress.Value; Progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null); Progress.Value = next;
                if (SystemParameters.ClientAreaAnimation && SystemParameters.UIEffects)
                    Progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new DoubleAnimation(old, next, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
            }
        });
        try
        {
            if (!_uninstall && InstallationService.IsProgramFilesTarget(target))
                await PrepareProgramFilesTargetAsync(target);
            _cancel.Token.ThrowIfCancellationRequested();
            var result = await Task.Run(() => _uninstall ? InstallationService.UninstallAsync(target, progress, _cancel.Token)
                : InstallationService.InstallAsync(_package!, target, desktop, startMenu, progress, _cancel.Token));
            Status.Text = result.Message; _finished = result.Success;
            if (result.Success) { Progress.IsIndeterminate = false; Progress.Value = 100; InstallButton.Content = "完成"; CancelButton.Visibility = Visibility.Collapsed; ActionTitle.Text = _uninstall ? "卸载完成" : "安装完成"; }
        }
        catch (OperationCanceledException) { Status.Text = "操作已取消，未完成的程序文件变更已恢复。"; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _busy = false; InstallButton.IsEnabled = true; if (!_finished) { Progress.IsIndeterminate = false; DirectoryPanel.IsEnabled = Shortcuts.IsEnabled = true; CancelButton.IsEnabled = true; CancelButton.Content = "关闭"; } }
    }
    private static async Task PrepareProgramFilesTargetAsync(string target)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定安装程序路径。");
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法读取当前 Windows 用户身份。");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var value in new[] { "--prepare-install-target", "--target", target, "--user-sid", sid })
            start.ArgumentList.Add(value);
        using var elevated = Process.Start(start) ?? throw new InvalidOperationException("无法启动安装权限准备程序。");
        await elevated.WaitForExitAsync();
        if (elevated.ExitCode != 0) throw new IOException("未能准备 Program Files 安装目录，请重试或选择其他位置。");
    }
    private void Cancel(object sender, RoutedEventArgs e) { if (!_busy) Close(); else { _cancel.Cancel(); CancelButton.IsEnabled = false; CancelButton.Content = "正在安全取消…"; } }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_busy) { e.Cancel = true; Cancel(this, new()); } }
}
