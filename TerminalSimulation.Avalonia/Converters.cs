using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TerminalSimulation.Avalonia.Converters
{
    public class InvertBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }
    }
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? true : false;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b ? true : false;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class WidthToColumnsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width && width > 0)
            {
                double minItemWidth = 320;
                if (parameter != null && double.TryParse(parameter.ToString(), out double parsedMin))
                {
                    minItemWidth = parsedMin;
                }
                
                double availableWidth = width - 24; // Buffer for scrollbar and margin
                if (availableWidth <= 0) availableWidth = width;

                int columns = (int)(availableWidth / minItemWidth);
                return Math.Max(1, columns);
            }
            return 1;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AspectRatioConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width && width > 0)
            {
                double ratio = 9.0 / 16.0; // Default to 16:9
                if (parameter != null && double.TryParse(parameter.ToString(), out double parsedRatio))
                {
                    ratio = parsedRatio;
                }
                return width * ratio;
            }
            return double.NaN;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IntEqualsVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue && parameter != null && int.TryParse(parameter.ToString(), out int targetValue))
            {
                return intValue == targetValue;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BrushOpacityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double opacity)
            {
                var isDark = global::Avalonia.Application.Current?.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark;
                var baseColor = isDark ? global::Avalonia.Media.Color.FromArgb(255, 30, 30, 30) : global::Avalonia.Media.Color.FromArgb(255, 250, 250, 250);
                return new global::Avalonia.Media.SolidColorBrush(baseColor) { Opacity = opacity };
            }
            return global::Avalonia.AvaloniaProperty.UnsetValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
