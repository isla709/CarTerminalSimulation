using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TerminalSimulation.Avalonia
{
    /// <summary>
    /// Attached behavior that applies a frosted glass effect to target elements.
    /// Uses BlurEffect in Avalonia instead of the WPF VisualBrush approach.
    /// NOTE: The property change detection uses direct element event subscription.
    /// </summary>
    public class GlassBehavior
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<GlassBehavior, Control, bool>(
                "IsEnabled",
                defaultValue: false);

        public static readonly AttachedProperty<Control?> RootImageProperty =
            AvaloniaProperty.RegisterAttached<GlassBehavior, Control, Control?>(
                "RootImage",
                defaultValue: null);

        public static void SetIsEnabled(AvaloniaObject element, bool value)
        {
            var oldValue = element.GetValue(IsEnabledProperty);
            element.SetValue(IsEnabledProperty, value);
            if (oldValue != value)
            {
                OnIsEnabledChanged(element, value);
            }
        }

        public static bool GetIsEnabled(AvaloniaObject element)
            => element.GetValue(IsEnabledProperty);

        public static void SetRootImage(AvaloniaObject element, Control? value)
            => element.SetValue(RootImageProperty, value);

        public static Control? GetRootImage(AvaloniaObject element)
            => element.GetValue(RootImageProperty);

        private static void OnIsEnabledChanged(AvaloniaObject obj, bool newValue)
        {
            if (obj is not Control element)
                return;

            if (newValue)
            {
                element.Loaded += Element_Loaded;
                if (element.IsAttachedToVisualTree())
                    UpdateGlass(element);
            }
            else
            {
                element.Loaded -= Element_Loaded;
                RemoveGlass(element);
            }
        }

        private static void Element_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is Control element)
                UpdateGlass(element);
        }

        private static void UpdateGlass(Control element)
        {
            element.Effect = new BlurEffect { Radius = 20 };

            if (element is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            }
            else if (element is Panel panel)
            {
                panel.Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            }
        }

        private static void RemoveGlass(Control element)
        {
            element.Effect = null;

            if (element is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            }
            else if (element is Panel panel)
            {
                panel.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            }
        }
    }
}
