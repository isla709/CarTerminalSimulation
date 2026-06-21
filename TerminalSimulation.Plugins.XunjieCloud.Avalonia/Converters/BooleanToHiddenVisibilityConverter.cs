using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace TerminalSimulation.Plugins.XunjieCloud.Avalonia.Converters
{
    public class BooleanToHiddenVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isVisible)
            {
                return isVisible ? true : false;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool visibility)
            {
                return visibility == true;
            }
            return false;
        }
    }
}
