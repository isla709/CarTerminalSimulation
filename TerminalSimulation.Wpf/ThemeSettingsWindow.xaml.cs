using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TerminalSimulation.Wpf
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
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosingAnimated)
            {
                e.Cancel = true; // Cancel immediate close

                var closeStoryboard = (Storyboard)FindResource("CloseStoryboard");
                if (closeStoryboard != null)
                {
                    closeStoryboard.Completed += (s, args) =>
                    {
                        _isClosingAnimated = true;
                        this.Close(); // Call close again, which will fall through next time
                    };
                    closeStoryboard.Begin(this);
                }
                else
                {
                    // Fallback if resource is not found
                    _isClosingAnimated = true;
                    this.Close();
                }
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}
