using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TerminalSimulation.PluginBase;
using System.Runtime.CompilerServices;

namespace TerminalSimulation.Wpf.Converters
{
    public class PluginToContentConverter : IValueConverter
    {
        private static readonly ConditionalWeakTable<IPlugin, ContentHolder> ContentCache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IPlugin plugin)
            {
                return ContentCache.GetValue(plugin, item => new ContentHolder(item.GetConfigurationPanel())).Content;
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        internal static void DisposeCachedContent()
        {
            foreach (var entry in ContentCache)
            {
                if (entry.Value.Content.DataContext is IDisposable disposable) disposable.Dispose();
            }
            ContentCache.Clear();
        }

        private sealed record ContentHolder(FrameworkElement Content);
    }
}
