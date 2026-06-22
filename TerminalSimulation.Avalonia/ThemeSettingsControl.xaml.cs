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

    private void ThemeSettingsControl_Loaded(object? sender, RoutedEventArgs e)
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
            new { Name = "科技蓝 (Blue)", Hex = "#2196F3", SukiColor = SukiColor.Blue },
            new { Name = "自然绿 (Green)", Hex = "#4CAF50", SukiColor = SukiColor.Green },
            new { Name = "活力橙 (Orange)", Hex = "#FF9800", SukiColor = SukiColor.Orange },
            new { Name = "激情红 (Red)", Hex = "#F44336", SukiColor = SukiColor.Red }
        };

        var items = new System.Collections.Generic.List<object>();
        foreach (var c in colors)
        {
            var color = Color.Parse(c.Hex);
            items.Add(new { Name = c.Name, Color = color, Brush = new SolidColorBrush(color), SukiColor = c.SukiColor });
        }
        ColorsItemsControl.ItemsSource = items;
    }

    private void RbLight_Checked(object? sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var app = Application.Current;
        if (app == null) return;
        app.RequestedThemeVariant = ThemeVariant.Light;
        SukiTheme.GetInstance().ChangeBaseTheme(ThemeVariant.Light);

    }

    private void RbDark_Checked(object? sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var app = Application.Current;
        if (app == null) return;
        app.RequestedThemeVariant = ThemeVariant.Dark;
        SukiTheme.GetInstance().ChangeBaseTheme(ThemeVariant.Dark);
    }

    private void ColorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext != null)
        {
            dynamic context = btn.DataContext;
            SukiColor sukiColor = context.SukiColor;
            SukiTheme.GetInstance().ChangeColorTheme(sukiColor);
        }
    }
}
