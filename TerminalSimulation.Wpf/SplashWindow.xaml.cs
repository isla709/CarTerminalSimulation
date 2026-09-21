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
            string[] splashImages =
            [
                "splash_dream_pool.png",
                "splash_pool_sunlight.png",
                "splash_pool_tyndall.png",
                "splash_liminal_office.png",
                "splash_flooded_concourse.png",
                "splash_weirdcore_playhall.png",
                "splash_starlit_hotel.png"
            ];
            var imageName = splashImages[Random.Shared.Next(splashImages.Length)];
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
