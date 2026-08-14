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
        Loaded += (_, _) => Attach(DataContext as MainViewModel);
        Unloaded += (_, _) => Attach(null);
        DataContextChanged += (_, e) => { if (IsLoaded) Attach(e.NewValue as MainViewModel); };
    }

    private void Attach(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel != null) _viewModel.LogMessages.CollectionChanged -= OnLogMessagesChanged;
        _viewModel = viewModel;
        if (_viewModel != null) _viewModel.LogMessages.CollectionChanged += OnLogMessagesChanged;
    }

    private void OnLogMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.AutoScrollLogs != true) return;
        _scrollTimer.Stop();
        _scrollTimer.Start();
    }

    private void ScrollToEnd()
    {
        _scrollTimer.Stop();
        if (LogListBox.Items.Count > 0) LogListBox.ScrollIntoView(LogListBox.Items[^1]);
    }
}
