using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TerminalSimulation.Wpf
{
    public static class GlassBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(GlassBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        public static readonly DependencyProperty RootImageProperty =
            DependencyProperty.RegisterAttached("RootImage", typeof(FrameworkElement), typeof(GlassBehavior), new PropertyMetadata(null));

        public static void SetRootImage(DependencyObject element, FrameworkElement value) => element.SetValue(RootImageProperty, value);
        public static FrameworkElement GetRootImage(DependencyObject element) => (FrameworkElement)element.GetValue(RootImageProperty);

        private static readonly System.Collections.Generic.Dictionary<FrameworkElement, EventHandler> _layoutHandlers = new();

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.Loaded += Element_Loaded;
                    EventHandler handler = (s, args) => UpdateGlass(element);
                    _layoutHandlers[element] = handler;
                    element.LayoutUpdated += handler;
                    UpdateGlass(element);
                }
                else
                {
                    element.Loaded -= Element_Loaded;
                    if (_layoutHandlers.TryGetValue(element, out var handler))
                    {
                        element.LayoutUpdated -= handler;
                        _layoutHandlers.Remove(element);
                    }
                    RemoveGlass(element);
                }
            }
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGlass((FrameworkElement)sender);
        }

        private static readonly DependencyProperty LastBoundsProperty =
            DependencyProperty.RegisterAttached("LastBounds", typeof(Rect), typeof(GlassBehavior), new PropertyMetadata(Rect.Empty));

        private static void UpdateGlass(FrameworkElement element)
        {
            if (!element.IsLoaded) return;
            var root = GetRootImage(element);
            if (root == null)
            {
                var window = Window.GetWindow(element);
                if (window != null)
                {
                    root = window.FindName("RootBlurredImage") as FrameworkElement;
                }
            }
            if (root == null) return;

            try
            {
                var transform = element.TransformToVisual(root);
                var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

                var lastBounds = (Rect)element.GetValue(LastBoundsProperty);
                if (bounds == lastBounds) return; // Prevent infinite updates
                element.SetValue(LastBoundsProperty, bounds);

                var brush = new VisualBrush(root)
                {
                    Viewbox = bounds,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, element.ActualWidth, element.ActualHeight),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.Fill
                };

                if (element is Border border)
                {
                    border.Background = brush;
                }
                else if (element is Control control)
                {
                    control.Background = brush;
                }
                else if (element is Panel panel)
                {
                    panel.Background = brush;
                }
            }
            catch { }
        }

        private static void RemoveGlass(FrameworkElement element)
        {
            if (element is Border border)
            {
                border.SetResourceReference(Border.BackgroundProperty, "MaterialDesignCardBackground");
            }
            else if (element is Control control)
            {
                control.SetResourceReference(Control.BackgroundProperty, "MaterialDesignCardBackground");
            }
            else if (element is Panel panel)
            {
                panel.SetResourceReference(Panel.BackgroundProperty, "MaterialDesignCardBackground");
            }
        }
    }
}
