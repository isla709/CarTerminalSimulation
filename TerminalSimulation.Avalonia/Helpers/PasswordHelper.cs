using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace TerminalSimulation.Avalonia.Helpers
{
    /// <summary>
    /// Attached behavior that enables two-way binding on a TextBox with PasswordChar set.
    /// In Avalonia, there is no PasswordBox control; instead, TextBox with PasswordChar='*'
    /// masks the text input. This helper bridges the Text property for password binding.
    /// NOTE: Uses direct Set method interception instead of Changed.Subscribe (no Rx dependency).
    /// </summary>
    public class PasswordHelper
    {
        public static readonly AttachedProperty<string> PasswordProperty =
            AvaloniaProperty.RegisterAttached<PasswordHelper, AvaloniaObject, string>(
                "Password",
                defaultValue: string.Empty,
                inherits: false,
                defaultBindingMode: BindingMode.TwoWay);

        public static readonly AttachedProperty<bool> AttachProperty =
            AvaloniaProperty.RegisterAttached<PasswordHelper, AvaloniaObject, bool>(
                "Attach",
                defaultValue: false);

        private static readonly AttachedProperty<bool> IsUpdatingProperty =
            AvaloniaProperty.RegisterAttached<PasswordHelper, AvaloniaObject, bool>(
                "IsUpdating");

        public static void SetAttach(AvaloniaObject element, bool value)
        {
            var oldValue = element.GetValue(AttachProperty);
            element.SetValue(AttachProperty, value);
            if (oldValue != value)
                OnAttachChanged(element, value, oldValue);
        }

        public static bool GetAttach(AvaloniaObject element)
            => element.GetValue(AttachProperty);

        public static string GetPassword(AvaloniaObject element)
            => element.GetValue(PasswordProperty);

        public static void SetPassword(AvaloniaObject element, string value)
        {
            var oldValue = element.GetValue(PasswordProperty);
            element.SetValue(PasswordProperty, value);
            if (oldValue != value)
                OnPasswordPropertyChanged(element, value);
        }

        private static bool GetIsUpdating(AvaloniaObject element)
            => element.GetValue(IsUpdatingProperty);

        private static void SetIsUpdating(AvaloniaObject element, bool value)
            => element.SetValue(IsUpdatingProperty, value);

        private static void OnPasswordPropertyChanged(AvaloniaObject obj, string newValue)
        {
            if (obj is TextBox textBox)
            {
                textBox.TextChanged -= OnTextChanged;

                if (!GetIsUpdating(textBox))
                {
                    textBox.Text = newValue;
                }
                textBox.TextChanged += OnTextChanged;
            }
        }

        private static void OnAttachChanged(AvaloniaObject obj, bool newValue, bool oldValue)
        {
            if (obj is not TextBox textBox)
                return;

            if (oldValue)
            {
                textBox.TextChanged -= OnTextChanged;
            }

            if (newValue)
            {
                textBox.TextChanged += OnTextChanged;
            }
        }

        private static void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                SetIsUpdating(textBox, true);
                SetPassword(textBox, textBox.Text ?? string.Empty);
                SetIsUpdating(textBox, false);
            }
        }
    }
}
