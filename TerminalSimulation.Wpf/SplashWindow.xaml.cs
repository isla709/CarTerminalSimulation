using System.Windows;

namespace TerminalSimulation.Wpf
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }
        
        public void SetLoadingText(string text)
        {
            Dispatcher.Invoke(() => {
                LoadingText.Text = text;
            });
        }
    }
}
