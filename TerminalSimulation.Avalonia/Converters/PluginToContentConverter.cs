using System;
using System.Globalization;
using Avalonia.Data.Converters;
using TerminalSimulation.PluginBase.Avalonia;

namespace TerminalSimulation.Avalonia.Converters
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

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
