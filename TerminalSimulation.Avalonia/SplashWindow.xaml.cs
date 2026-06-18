using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TerminalSimulation.Avalonia
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void SetLoadingText(string text)
        {
            Dispatcher.UIThread.InvokeAsync(() => {
                LoadingText.Text = text;
            });
        }
    }
}
