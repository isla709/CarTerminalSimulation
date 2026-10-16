using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalSimulation.Wpf.Services;

namespace TerminalSimulation.Wpf.ViewModels;

public partial class SystemLogViewModel : ObservableObject, IDisposable
{
    private readonly ApplicationLog _log;
    private readonly Dispatcher _dispatcher;
    private int _refreshPending;
    private bool _disposed;
    public ObservableCollection<LogMessageItem> Entries { get; } = new();
    public ICollectionView VisibleEntries { get; }
    [ObservableProperty] private bool _showCommunication = true;
    [ObservableProperty] private bool _showSystem = true;
    [ObservableProperty] private bool _showPlugin = true;
    public string CountSummary => $"显示 {VisibleEntries.Cast<object>().Count()} / {Entries.Count} 条";

    public SystemLogViewModel() : this(ApplicationLog.Shared, Dispatcher.CurrentDispatcher) { }
    internal SystemLogViewModel(ApplicationLog log, Dispatcher dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;
        VisibleEntries = new ListCollectionView(Entries) { Filter = Accept };
        _log.Changed += ScheduleRefresh;
        RefreshFromHistory();
    }

    private bool Accept(object value) => value is LogMessageItem item && (item.Category switch
    {
        ApplicationLogCategory.Communication => ShowCommunication,
        ApplicationLogCategory.Plugin => ShowPlugin,
        _ => ShowSystem
    });

    partial void OnShowCommunicationChanged(bool value) => RefreshFilter();
    partial void OnShowSystemChanged(bool value) => RefreshFilter();
    partial void OnShowPluginChanged(bool value) => RefreshFilter();
    private void RefreshFilter() { VisibleEntries.Refresh(); OnPropertyChanged(nameof(CountSummary)); }

    [RelayCommand]
    private void ShowAllCategories() { ShowCommunication = true; ShowSystem = true; ShowPlugin = true; }
    public void Clear() { _log.Clear(); RefreshFromHistory(); }

    private void ScheduleRefresh()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        // One pending callback, not one callback per packet. A hidden/busy page
        // never accumulates an unbounded dispatcher queue of log records.
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshFromHistory));
    }

    internal void RefreshFromHistory()
    {
        Interlocked.Exchange(ref _refreshPending, 0);
        if (_disposed) return;
        var history = _log.Snapshot();
        if (history.Length == 0) Entries.Clear();
        else
        {
            while (Entries.Count > 0 && Entries[0].Sequence < history[0].Sequence) Entries.RemoveAt(0);
            var last = Entries.Count == 0 ? 0 : Entries[^1].Sequence;
            foreach (var entry in history.Where(e => e.Sequence > last))
                Entries.Add(new LogMessageItem {
                    Sequence = entry.Sequence, Category = entry.Category,
                    TimestampStr = $"[{entry.Timestamp:HH:mm:ss.fff}]", DirectionStr = $"[{entry.Source}]",
                    Message = entry.Message, HasRaw = entry.RawData.Length > 0, RawData = entry.RawData
                });
        }
        OnPropertyChanged(nameof(CountSummary));
    }

    public void Dispose() { _disposed = true; _log.Changed -= ScheduleRefresh; }
}
