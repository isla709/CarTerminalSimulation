using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TerminalSimulation.Wpf
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            var workArea = SystemParameters.WorkArea;
            var scale = Math.Min(1d, Math.Min(workArea.Width * 0.9 / Width, workArea.Height * 0.9 / Height));
            Width *= scale;
            Height *= scale;
            var imageName = $"splash_{Random.Shared.Next(1, 4)}.jpg";
            SplashBackground.ImageSource = new BitmapImage(
                new Uri($"pack://application:,,,/{imageName}", UriKind.Absolute));
            VersionText.Text = AppVersionInfo.FullVersion;
        }
        
        public void SetLoadingText(string text)
        {
            Dispatcher.Invoke(() => {
                LoadingText.Text = text;
            });
        }
    }
}
