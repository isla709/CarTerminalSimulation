using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using SukiUI;
using SukiUI.Enums;

namespace TerminalSimulation.Avalonia;

public partial class ThemeSettingsControl : UserControl
{
    public ThemeSettingsControl()
    {
        InitializeComponent();
        this.Loaded += ThemeSettingsControl_Loaded;
    }

    private void ThemeSettingsControl_Loaded(object sender, RoutedEventArgs e)
    {
        LoadCurrentTheme();
        LoadColors();
    }

    private void LoadCurrentTheme()
    {
        var app = Application.Current;
        if (app != null && app.RequestedThemeVariant == ThemeVariant.Dark)
        {
            RbDark.IsChecked = true;
        }
        else
        {
            RbLight.IsChecked = true;
        }
    }

    private void LoadColors()
    {
        if (ColorsItemsControl.ItemsSource != null) return;

        var colors = new[]
        {
            new { Name = "红 (Red)", Hex = "#F44336" },
            new { Name = "粉 (Pink)", Hex = "#E91E63" },
            new { Name = "紫 (Purple)", Hex = "#9C27B0" },
            new { Name = "深紫 (DeepPurple)", Hex = "#673AB7" },
            new { Name = "靛蓝 (Indigo)", Hex = "#3F51B5" },
            new { Name = "蓝 (Blue)", Hex = "#2196F3" },
            new { Name = "浅蓝 (LightBlue)", Hex = "#03A9F4" },
            new { Name = "青色 (Cyan)", Hex = "#00BCD4" },
            new { Name = "青绿 (Teal)", Hex = "#009688" },
            new { Name = "绿 (Green)", Hex = "#4CAF50" },
            new { Name = "浅绿 (LightGreen)", Hex = "#8BC34A" },
            new { Name = "石灰 (Lime)", Hex = "#CDDC39" },
            new { Name = "黄 (Yellow)", Hex = "#FFEB3B" },
            new { Name = "琥珀 (Amber)", Hex = "#FFC107" },
            new { Name = "橙 (Orange)", Hex = "#FF9800" },
            new { Name = "深橙 (DeepOrange)", Hex = "#FF5722" },
            new { Name = "棕 (Brown)", Hex = "#795548" },
            new { Name = "灰 (Grey)", Hex = "#9E9E9E" },
            new { Name = "蓝灰 (BlueGrey)", Hex = "#607D8B" }
        };

        var items = new System.Collections.Generic.List<object>();
        foreach (var c in colors)
        {
            var color = Color.Parse(c.Hex);
            items.Add(new { Name = c.Name, Color = color, Brush = new SolidColorBrush(color) });
        }
        ColorsItemsControl.ItemsSource = items;
    }

    private void ThemeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        var app = Application.Current;
        if (app == null) return;

        app.RequestedThemeVariant = RbDark.IsChecked == true
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        SukiTheme.GetInstance().ChangeBaseTheme(app.RequestedThemeVariant);
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Color color)
        {
            // Map color to the closest SukiColor
            var sukiColor = GetClosestSukiColor(color);
            SukiTheme.GetInstance().ChangeColorTheme(sukiColor);
        }
    }

    private static SukiColor GetClosestSukiColor(Color color)
    {
        // Simple hue-based mapping to the 4 built-in SukiUI colors
        var hue = GetHue(color);
        return hue switch
        {
            >= 0 and < 30 or >= 330 => SukiColor.Red,
            >= 30 and < 90 => SukiColor.Orange,
            >= 90 and < 200 => SukiColor.Green,
            _ => SukiColor.Blue,
        };
    }

    private static double GetHue(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        if (delta == 0) return 0;
        double hue;
        if (max == r) hue = ((g - b) / delta) % 6;
        else if (max == g) hue = (b - r) / delta + 2;
        else hue = (r - g) / delta + 4;
        hue *= 60;
        if (hue < 0) hue += 360;
        return hue;
    }

    private void ThemeImageBorder_Tapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext != null)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.SelectThemeImageCommand.Execute(border.DataContext);
            }
        }
    }
}
