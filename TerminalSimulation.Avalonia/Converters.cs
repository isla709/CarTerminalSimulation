using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TerminalSimulation.Avalonia.Converters
{
    public class InvertBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }
    }
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? true : false;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b ? true : false;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class WidthToColumnsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AspectRatioConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
