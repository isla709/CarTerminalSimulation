using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TerminalSimulation.Wpf;

/// <summary>
/// Projects the window's pre-blurred background into a card so that cards retain
/// the glass appearance without applying a costly blur effect to every card.
/// </summary>
public static class GlassBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(GlassBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty RootImageProperty =
        DependencyProperty.RegisterAttached(
            "RootImage",
            typeof(FrameworkElement),
            typeof(GlassBehavior),
            new PropertyMetadata(null, OnRootImageChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, GlassState> States = new();

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetRootImage(DependencyObject element, FrameworkElement? value) =>
        element.SetValue(RootImageProperty, value);

    public static FrameworkElement? GetRootImage(DependencyObject element) =>
        (FrameworkElement?)element.GetValue(RootImageProperty);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element) return;

        var state = States.GetValue(element, static owner => new GlassState(owner));
        if ((bool)e.NewValue)
        {
            state.Attach();
        }
        else
        {
            state.Detach();
            RestoreThemeBackground(element);
        }
    }

    private static void OnRootImageChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FrameworkElement element &&
            States.TryGetValue(element, out var state) &&
            GetIsEnabled(element))
        {
            state.InvalidateRoot();
        }
    }

    private static void RestoreThemeBackground(FrameworkElement element)
    {
        switch (element)
        {
            case Border border:
                border.SetResourceReference(Border.BackgroundProperty, "MaterialDesignCardBackground");
                break;
            case Control control:
                control.SetResourceReference(Control.BackgroundProperty, "MaterialDesignCardBackground");
                break;
            case Panel panel:
                panel.SetResourceReference(Panel.BackgroundProperty, "MaterialDesignCardBackground");
                break;
        }
    }

    private sealed class GlassState
    {
        private readonly FrameworkElement _owner;
        private FrameworkElement? _root;
        private Rect _lastBounds = Rect.Empty;
        private bool _attached;
        private bool _refreshQueued;

        public GlassState(FrameworkElement owner) => _owner = owner;

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _owner.Loaded += OnLoaded;
            _owner.Unloaded += OnUnloaded;
            _owner.SizeChanged += OnGeometryChanged;
            if (_owner.IsLoaded) AttachRootAndQueueRefresh();
        }

        public void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _owner.Loaded -= OnLoaded;
            _owner.Unloaded -= OnUnloaded;
            _owner.SizeChanged -= OnGeometryChanged;
            DetachRoot();
            _lastBounds = Rect.Empty;
        }

        public void InvalidateRoot()
        {
            DetachRoot();
            _lastBounds = Rect.Empty;
            if (_owner.IsLoaded) AttachRootAndQueueRefresh();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => AttachRootAndQueueRefresh();

        private void OnUnloaded(object sender, RoutedEventArgs e) => DetachRoot();

        private void OnGeometryChanged(object sender, SizeChangedEventArgs e) => QueueRefresh();

        private void OnRootLayoutUpdated(object? sender, EventArgs e) => QueueRefresh();

        private void AttachRootAndQueueRefresh()
        {
            var explicitRoot = GetRootImage(_owner);
            var root = explicitRoot ?? Window.GetWindow(_owner)?.FindName("RootBlurredImage") as FrameworkElement;
            if (!ReferenceEquals(root, _root))
            {
                DetachRoot();
                _root = root;
                if (_root != null) _root.LayoutUpdated += OnRootLayoutUpdated;
            }
            QueueRefresh();
        }

        private void DetachRoot()
        {
            if (_root != null) _root.LayoutUpdated -= OnRootLayoutUpdated;
            _root = null;
        }

        private void QueueRefresh()
        {
            if (!_attached || _refreshQueued || !_owner.IsLoaded) return;
            _refreshQueued = true;
            _owner.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _refreshQueued = false;
                Refresh();
            }));
        }

        private void Refresh()
        {
            if (!_attached || _root == null || !_root.IsLoaded ||
                _owner.ActualWidth <= 0 || _owner.ActualHeight <= 0)
            {
                return;
            }

            try
            {
                var bounds = _owner.TransformToVisual(_root)
                    .TransformBounds(new Rect(0, 0, _owner.ActualWidth, _owner.ActualHeight));
                if (bounds == _lastBounds) return;
                _lastBounds = bounds;

                var brush = new VisualBrush(_root)
                {
                    Viewbox = bounds,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, _owner.ActualWidth, _owner.ActualHeight),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                if (brush.CanFreeze) brush.Freeze();

                switch (_owner)
                {
                    case Border border:
                        border.Background = brush;
                        break;
                    case Control control:
                        control.Background = brush;
                        break;
                    case Panel panel:
                        panel.Background = brush;
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                // The visual may briefly move between presentation sources during DPI changes.
                _lastBounds = Rect.Empty;
            }
        }
    }
}
