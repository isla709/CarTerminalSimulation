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
        private bool _isClosingAnimated = false;

        public ThemeSettingsWindow()
        {
            InitializeComponent();
            this.Closing += ThemeSettingsWindow_Closing;
        }

        private async void ThemeSettingsWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (!_isClosingAnimated)
            {
                e.Cancel = true;
                _isClosingAnimated = true;

                var animation = new global::Avalonia.Animation.Animation
                {
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = global::Avalonia.Animation.FillMode.Forward,
                    Children =
                    {
                        new global::Avalonia.Animation.KeyFrame
                        {
                            Cue = new global::Avalonia.Animation.Cue(0d),
                            Setters = { new global::Avalonia.Styling.Setter(Window.OpacityProperty, 1.0d) }
                        },
                        new global::Avalonia.Animation.KeyFrame
                        {
                            Cue = new global::Avalonia.Animation.Cue(1d),
                            Setters = { new global::Avalonia.Styling.Setter(Window.OpacityProperty, 0.0d) }
                        }
                    }
                };

                await animation.RunAsync(this);
                this.Close();
            }
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
