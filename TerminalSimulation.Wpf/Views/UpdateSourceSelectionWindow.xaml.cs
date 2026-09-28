using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TerminalSimulation.Wpf.Services.Updates;

namespace TerminalSimulation.Wpf.Views;

public partial class UpdateSourceSelectionWindow : Window
{
    private readonly UpdateCandidate _candidate;
    private readonly UpdateSourceProbeService _probeService;
    private readonly ObservableCollection<SourceProbeItem> _items = [];
    private CancellationTokenSource? _probeCancellation;

    internal UpdateDownloadSource? SelectedSource { get; private set; }

    internal UpdateSourceSelectionWindow(
        UpdateCandidate candidate,
        UpdateSourceProbeService? probeService = null)
    {
        InitializeComponent();
        _candidate = candidate;
        _probeService = probeService ?? new UpdateSourceProbeService();
        SubtitleText.Text = $"{candidate.Version} · 共 {candidate.Sources.Count} 个可用来源";
        SourceList.ItemsSource = _items;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ProbeAllAsync();

    private async Task ProbeAllAsync()
    {
        _probeCancellation?.Cancel();
        _probeCancellation?.Dispose();
        var probeCancellation = new CancellationTokenSource();
        _probeCancellation = probeCancellation;
        SelectedSource = null;
        UseSourceButton.IsEnabled = false;
        ProbeProgress.IsIndeterminate = true;
        ProbeSummaryText.Text = "正在并行检测所有下载源…";
        _items.Clear();
        foreach (var source in _candidate.Sources.OrderByDescending(item => item.Priority))
            _items.Add(SourceProbeItem.Checking(source));

        try
        {
            var tasks = _candidate.Sources.Select(source =>
                _probeService.ProbeAsync(source, probeCancellation.Token));
            var results = await Task.WhenAll(tasks);
            var ranked = UpdateSourceProbeService.Rank(results);

            _items.Clear();
            var best = ranked.FirstOrDefault(result => result.IsAvailable);
            foreach (var result in ranked)
                _items.Add(SourceProbeItem.FromResult(result, ReferenceEquals(result, best)));

            ProbeProgress.IsIndeterminate = false;
            ProbeProgress.Value = 100;
            var availableCount = ranked.Count(result => result.IsAvailable);
            ProbeSummaryText.Text = availableCount > 0
                ? $"{availableCount}/{ranked.Count} 个来源可用，已为你选择综合表现最好的来源"
                : "所有来源均暂时无法连接，可以重新检测或稍后再试";
            if (best is not null)
                SourceList.SelectedItem = _items.First(item => item.Source == best.Source);
        }
        catch (OperationCanceledException) when (probeCancellation.IsCancellationRequested)
        {
        }
    }

    private void SourceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SourceList.SelectedItem is SourceProbeItem { IsAvailable: true } item)
        {
            SelectedSource = item.Source;
            UseSourceButton.IsEnabled = true;
        }
        else
        {
            SelectedSource = null;
            UseSourceButton.IsEnabled = false;
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await ProbeAllAsync();

    private void UseSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSource is null) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_Closing(object? sender, CancelEventArgs e) => _probeCancellation?.Cancel();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private sealed class SourceProbeItem : INotifyPropertyChanged
    {
        public required UpdateDownloadSource Source { get; init; }
        public string Name => Source.Name;
        public string Host => Source.Host;
        public required bool IsAvailable { get; init; }
        public required string StatusText { get; init; }
        public required string MetricsText { get; init; }
        public required Brush StatusBrush { get; init; }
        public required Visibility RecommendedVisibility { get; init; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static SourceProbeItem Checking(UpdateDownloadSource source) => new()
        {
            Source = source,
            IsAvailable = false,
            StatusText = "检测中…",
            MetricsText = "正在连接",
            StatusBrush = new SolidColorBrush(Color.FromRgb(117, 117, 117)),
            RecommendedVisibility = Visibility.Collapsed
        };

        public static SourceProbeItem FromResult(UpdateSourceProbeResult result, bool recommended) => new()
        {
            Source = result.Source,
            IsAvailable = result.IsAvailable,
            StatusText = result.IsAvailable ? "可用" : "不可用",
            MetricsText = result.IsAvailable
                ? $"{result.LatencyMilliseconds} ms · {FormatSpeed(result.BytesPerSecond)}"
                : result.Detail,
            StatusBrush = new SolidColorBrush(result.IsAvailable
                ? Color.FromRgb(46, 125, 50)
                : Color.FromRgb(211, 47, 47)),
            RecommendedVisibility = recommended ? Visibility.Visible : Visibility.Collapsed
        };

        private static string FormatSpeed(double? bytesPerSecond)
        {
            if (bytesPerSecond is null or <= 0) return "已连接";
            if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
            return $"{bytesPerSecond / 1024:0} KB/s";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
