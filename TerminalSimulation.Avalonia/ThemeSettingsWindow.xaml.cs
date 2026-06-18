using System;
using Avalonia;
using Avalonia.Controls;
// TODO: Use Avalonia.Animation when reimplementing close animation
// using Avalonia.Animation;

namespace TerminalSimulation.Avalonia
{
    /// <summary>
    /// ThemeSettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ThemeSettingsWindow : Window
    {
        // TODO: Reimplement close animation with Avalonia.Animation API
        // private bool _isClosingAnimated = false;

        public ThemeSettingsWindow()
        {
            InitializeComponent();
            this.Closing += ThemeSettingsWindow_Closing;
        }

        private void ThemeSettingsWindow_Closing(object sender, WindowClosingEventArgs e)
        {
            // TODO: Reimplement close animation with Avalonia.Animation API using Animation
            // class with KeyFrame elements. The WPF version used Storyboard/DoubleAnimation
            // for a fade-out effect triggered via FindResource("CloseStoryboard").
        }

        private void BgOverlay_Tapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
        {
            // Close the settings dialog when clicking the background overlay
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.CloseSettingsCommand.Execute(null);
            }
        }
    }
}
