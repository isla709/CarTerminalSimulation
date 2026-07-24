using System.Windows;
using System;
using System.Windows.Media.Imaging;

namespace TerminalSimulation.Wpf
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            var imageName = $"splash_{Random.Shared.Next(1, 4)}.jpg";
            SplashBackground.ImageSource = new BitmapImage(
                new Uri($"pack://application:,,,/{imageName}", UriKind.Absolute));
        }
        
        public void SetLoadingText(string text)
        {
            Dispatcher.Invoke(() => {
                LoadingText.Text = text;
            });
        }
    }
}
