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
    private string _updateToolTip = "检查更新";

    [RelayCommand]
    private Task CheckForUpdatesAsync() => TrackUpdateCheck(CheckForUpdatesCoreAsync(showNoUpdateMessage: true, _updateCancellation.Token));

    internal Task CheckForUpdatesSilentlyAsync() => TrackUpdateCheck(CheckForUpdatesSilentlyCoreAsync());

    private async Task CheckForUpdatesSilentlyCoreAsync()
    {
        try
        {
            var service = GetUpdateService();
            var configuration = await service.LoadConfigurationAsync(_updateCancellation.Token);
            if (!configuration.AutoCheck || !IsAutoCheckDue(configuration.CheckIntervalHours)) return;
            await CheckForUpdatesCoreAsync(showNoUpdateMessage: false, _updateCancellation.Token);
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _appLogger.Error("自动更新", "自动检查更新失败", ex);
        }
    }

    private async Task CheckForUpdatesCoreAsync(bool showNoUpdateMessage, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _updateCheckActive, 1, 0) != 0) return;
        IsCheckingForUpdates = true;
        UpdateToolTip = "正在检查更新…";
        try
        {
            var result = await GetUpdateService().CheckForUpdatesAsync(AppVersionInfo.FullVersion, cancellationToken);
            WriteLastUpdateCheck();
            if (result.Candidate is null)
            {
                IsUpdateAvailable = false;
                UpdateToolTip = result.HasEnabledSources ? "已是最新版本" : "未配置可用更新源";
                if (showNoUpdateMessage)
                {
                    var detail = result.HasEnabledSources
                        ? result.Diagnostics.Count == 0
                            ? "当前已经是最新版本。"
                            : $"未发现可用更新。\n\n部分更新源不可用：\n{string.Join("\n", result.Diagnostics)}"
                        : $"没有启用更新源。请编辑：\n{Path.Combine(AppContext.BaseDirectory, "update-sources.json")}";
                    MessageBox.Show(detail, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            IsUpdateAvailable = true;
            UpdateToolTip = $"发现新版本 {result.Candidate.Version}";
            var owner = Application.Current.MainWindow;
            var dialog = new UpdateAvailableWindow(AppVersionInfo.FullVersion, result.Candidate)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                UpdateLauncher.Start(result.Candidate);
                owner?.Close();
            }
            catch (Exception ex)
            {
                _appLogger.Error("自动更新", "启动更新程序失败", ex);
                MessageBox.Show(ex.Message, "无法启动更新", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UpdateToolTip = "检查更新";
        }
        catch (Exception ex)
        {
            UpdateToolTip = "检查更新失败";
            _appLogger.Error("自动更新", "检查更新失败", ex);
            if (showNoUpdateMessage)
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
