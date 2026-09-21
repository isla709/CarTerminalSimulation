using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Windows;
using TerminalSimulation.Wpf.Services.Updates;
using TerminalSimulation.Wpf.Views;

namespace TerminalSimulation.Wpf.ViewModels;

public partial class MainViewModel
{
    private UpdateService? _updateService;
    private int _updateCheckActive;
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly object _updateTaskSync = new();
    private Task _activeUpdateCheckTask = Task.CompletedTask;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateToolTip = "查看可用版本";

    [ObservableProperty]
    private bool _includePrereleaseUpdates;

    public bool IsStableRelease => string.Equals(
        AppVersionInfo.UpdateChannel,
        "stable",
        StringComparison.OrdinalIgnoreCase);

    partial void OnIncludePrereleaseUpdatesChanged(bool value)
    {
        if (!_isLoadingConfig)
            SaveConfigDebounced();
    }

    [RelayCommand]
    private Task CheckForUpdatesAsync() => TrackUpdateCheck(CheckForUpdatesCoreAsync(interactive: true, _updateCancellation.Token));

    internal Task CheckForUpdatesSilentlyAsync() => TrackUpdateCheck(CheckForUpdatesSilentlyCoreAsync());

    private async Task CheckForUpdatesSilentlyCoreAsync()
    {
        try
        {
            var service = GetUpdateService();
            var configuration = await service.LoadConfigurationAsync(_updateCancellation.Token);
            if (!configuration.AutoCheck || !IsAutoCheckDue(configuration.CheckIntervalHours)) return;
            await CheckForUpdatesCoreAsync(interactive: false, _updateCancellation.Token);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _appLogger.Error("自动更新", "自动检查更新失败", ex);
        }
    }

    private async Task CheckForUpdatesCoreAsync(bool interactive, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _updateCheckActive, 1, 0) != 0) return;
        IsCheckingForUpdates = true;
        UpdateToolTip = "正在检查更新…";
        try
        {
            var result = await GetUpdateService().CheckForUpdatesAsync(
                AppVersionInfo.FullVersion,
                AppVersionInfo.UpdateLine,
                AppVersionInfo.CompatibilityEpoch,
                cancellationToken,
                AppVersionInfo.UpdateChannel,
                IncludePrereleaseUpdates);
            WriteLastUpdateCheck();

            IsUpdateAvailable = result.Candidate is not null;
            UpdateToolTip = result.Candidate is not null
                ? "有可选更新"
                : result.Candidates.Count > 0
                    ? $"可选择 {result.Candidates.Count} 个历史版本"
                    : result.HasEnabledSources
                        ? "当前版本线暂无其他版本"
                        : "未配置可用更新源";

            // Automatic checks are informational only. Never interrupt startup
            // or pressure the user with an update dialog.
            if (!interactive) return;

            if (result.Candidates.Count == 0)
            {
                var detail = result.HasEnabledSources
                    ? result.Diagnostics.Count == 0
                        ? $"版本线 {AppVersionInfo.UpdateLine} 暂无其他可用版本。"
                        : $"未发现可用版本。\n\n部分更新源不可用：\n{string.Join("\n", result.Diagnostics)}"
                    : $"没有启用更新源。请编辑：\n{Path.Combine(AppContext.BaseDirectory, "update-sources.json")}";
                MessageBox.Show(detail, "版本管理", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mainWindow = Application.Current.MainWindow;
            var dialogOwner = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive) ?? mainWindow;
            var dialog = new UpdateAvailableWindow(
                AppVersionInfo.FullVersion,
                AppVersionInfo.UpdateLine,
                AppVersionInfo.CompatibilityEpoch,
                result.Candidates,
                result.Candidate)
            {
                Owner = dialogOwner
            };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidate is not { } selectedCandidate) return;

            try
            {
                UpdateLauncher.Start(
                    selectedCandidate,
                    AppVersionInfo.UpdateLine,
                    AppVersionInfo.CompatibilityEpoch);
                mainWindow?.Close();
            }
            catch (Exception ex)
            {
                _appLogger.Error("自动更新", "启动更新程序失败", ex);
                MessageBox.Show(ex.Message, "无法启动更新", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UpdateToolTip = "查看可用版本";
        }
        catch (Exception ex)
        {
            UpdateToolTip = "检查更新失败";
            _appLogger.Error("自动更新", "检查更新失败", ex);
            if (interactive)
                MessageBox.Show($"检查更新失败：{ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsCheckingForUpdates = false;
            Interlocked.Exchange(ref _updateCheckActive, 0);
        }
    }

    private UpdateService GetUpdateService() => _updateService ??= new UpdateService(_appLogger);

    private Task TrackUpdateCheck(Task task)
    {
        lock (_updateTaskSync)
        {
            _activeUpdateCheckTask = Task.WhenAll(_activeUpdateCheckTask, task);
        }
        return task;
    }

    private void CancelUpdateOperations() => _updateCancellation.Cancel();

    private async Task WaitForUpdateOperationsAsync()
    {
        Task activeTask;
        lock (_updateTaskSync) { activeTask = _activeUpdateCheckTask; }
        try { await activeTask; }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        _updateCancellation.Dispose();
    }

    private static string LastCheckPath => Path.Combine(
        AppContext.BaseDirectory,
        "Workspaces",
        "Updates",
        ".last-check");

    private static bool IsAutoCheckDue(double intervalHours)
    {
        try
        {
            if (!File.Exists(LastCheckPath)) return true;
            if (!DateTimeOffset.TryParse(File.ReadAllText(LastCheckPath), out var timestamp)) return true;
            return DateTimeOffset.UtcNow - timestamp.ToUniversalTime() >= TimeSpan.FromHours(intervalHours);
        }
        catch
        {
            return true;
        }
    }

    private static void WriteLastUpdateCheck()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastCheckPath)!);
            File.WriteAllText(LastCheckPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // An unwritable timestamp only means the next launch checks again.
        }
    }
}
