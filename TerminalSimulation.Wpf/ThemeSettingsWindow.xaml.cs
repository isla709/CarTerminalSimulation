using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TerminalSimulation.Wpf.Helpers;

namespace TerminalSimulation.Wpf
{
    /// <summary>
    /// ThemeSettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ThemeSettingsWindow : Window
    {
        private bool _isClosingAnimated = false;
        private int _lastSelectedTabIndex;

        public ThemeSettingsWindow()
        {
            InitializeComponent();
            _lastSelectedTabIndex = SettingsTabControl.SelectedIndex;
        }

        private void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != SettingsTabControl)
            {
                return;
            }

            var selectedIndex = SettingsTabControl.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex == _lastSelectedTabIndex)
            {
                return;
            }

            var direction = selectedIndex > _lastSelectedTabIndex ? 1d : -1d;
            _lastSelectedTabIndex = selectedIndex;

            // Apply the animated value in the selection event itself. Deferring this
            // until Loaded allows one frame at the settled position to be rendered,
            // which looks like the page briefly pulls backwards before sliding in.
            if (SettingsTabControl.SelectedItem is TabItem selectedTab &&
                selectedTab.Content is FrameworkElement content)
            {
                TabTransitionAnimator.Animate(content, direction);
            }
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
