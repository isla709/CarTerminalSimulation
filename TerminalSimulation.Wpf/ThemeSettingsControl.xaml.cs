using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace TerminalSimulation.Wpf;

public partial class ThemeSettingsControl : UserControl
{
    private readonly PaletteHelper _paletteHelper = new PaletteHelper();

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
        var theme = _paletteHelper.GetTheme();
        if (theme.GetBaseTheme() == BaseTheme.Dark)
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
        if (ColorsItemsControl.ItemsSource != null) return; // Prevent double load

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
            var color = (Color)ColorConverter.ConvertFromString(c.Hex);
            items.Add(new { Name = c.Name, Color = color, Brush = new SolidColorBrush(color) });
        }
        ColorsItemsControl.ItemsSource = items;
    }

    private void ThemeMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var theme = _paletteHelper.GetTheme();
        if (RbDark.IsChecked == true)
        {
            theme.SetBaseTheme(BaseTheme.Dark);
        }
        else
        {
            theme.SetBaseTheme(BaseTheme.Light);
        }
        _paletteHelper.SetTheme(theme);
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Color color)
        {
            var theme = _paletteHelper.GetTheme();
            theme.SetPrimaryColor(color);
            theme.SetSecondaryColor(color);
            _paletteHelper.SetTheme(theme);
        }
    }
}
