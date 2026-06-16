using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TerminalSimulation.PluginBase;

namespace TerminalSimulation.Wpf.Converters
{
    public class PluginToContentConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IPlugin plugin)
            {
                return plugin.GetConfigurationPanel();
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
