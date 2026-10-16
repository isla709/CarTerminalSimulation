using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TerminalSimulation.Wpf.ViewModels;

namespace TerminalSimulation.Wpf.Views;

public partial class CommunicationLogView : UserControl
{
    private readonly DispatcherTimer _scrollTimer;
    private MainViewModel? _viewModel;

    public CommunicationLogView()
    {
        InitializeComponent();
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Background, (_, _) => ScrollToEnd(), Dispatcher);
        _scrollTimer.Stop();
        Loaded += (_, _) => Attach(DataContext as MainViewModel);
        Unloaded += (_, _) => { _scrollTimer.Stop(); Attach(null); };
        DataContextChanged += (_, e) => { if (IsLoaded) Attach(e.NewValue as MainViewModel); };
    }

    private void Attach(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel != null) ((INotifyCollectionChanged)_viewModel.SystemLogs.VisibleEntries).CollectionChanged -= OnLogMessagesChanged;
        _viewModel = viewModel;
        if (_viewModel != null) ((INotifyCollectionChanged)_viewModel.SystemLogs.VisibleEntries).CollectionChanged += OnLogMessagesChanged;
        OnLogMessagesChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnLogMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _scrollTimer.Stop();
        if (_viewModel?.AutoScrollLogs != true || !IsLoaded) return;
        _scrollTimer.Start();
    }

    private void ScrollToEnd()
    {
        _scrollTimer.Stop();
        if (_viewModel?.AutoScrollLogs != true || !IsLoaded) return;
        if (LogListBox.Items.Count > 0) LogListBox.ScrollIntoView(LogListBox.Items[^1]);
    }
}
