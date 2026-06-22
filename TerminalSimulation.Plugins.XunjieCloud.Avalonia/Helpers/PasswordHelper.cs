using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using System;

namespace TerminalSimulation.Plugins.XunjieCloud.Avalonia.Helpers
{
    public class PasswordHelper
    {
        public static readonly AttachedProperty<string> PasswordProperty =
            AvaloniaProperty.RegisterAttached<PasswordHelper, TextBox, string>(
                "Password", string.Empty, false, BindingMode.TwoWay);

        public static readonly AttachedProperty<bool> AttachProperty =
            AvaloniaProperty.RegisterAttached<PasswordHelper, TextBox, bool>(
                "Attach", false);

        static PasswordHelper()
        {
            AttachProperty.Changed.AddClassHandler<TextBox>(OnAttachChanged);
        }

        public static string GetPassword(AvaloniaObject element)
        {
            return element.GetValue(PasswordProperty);
        }

        public static void SetPassword(AvaloniaObject element, string value)
        {
            element.SetValue(PasswordProperty, value);
        }

        public static bool GetAttach(AvaloniaObject element)
        {
            return element.GetValue(AttachProperty);
        }

        public static void SetAttach(AvaloniaObject element, bool value)
        {
            element.SetValue(AttachProperty, value);
        }

        private static void OnAttachChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool attach && attach)
            {
                textBox.TextChanged += PasswordChanged;
            }
            else
            {
                textBox.TextChanged -= PasswordChanged;
            }
        }

        private static void PasswordChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is TextBox passwordBox)
            {
                SetPassword(passwordBox, passwordBox.Text ?? "");
            }
        }
    }
}
