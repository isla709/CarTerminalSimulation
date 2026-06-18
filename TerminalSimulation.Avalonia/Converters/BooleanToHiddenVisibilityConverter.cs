using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TerminalSimulation.Avalonia.Converters
{
    /// <summary>
    /// Converts bool to IsVisible (true = Visible, false = Hidden but still in layout).
    /// In Avalonia, visibility is controlled via IsVisible (bool) property.
    /// To hide while keeping layout space, use Opacity=0 or a custom approach.
    /// </summary>
    public class BooleanToHiddenVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isVisible)
            {
                return isVisible;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isVisible)
            {
                return isVisible;
            }
            return false;
        }
    }
}
