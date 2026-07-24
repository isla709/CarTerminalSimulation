using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using Avalonia.Platform;

namespace TerminalSimulation.Avalonia
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            var imageName = $"splash_{Random.Shared.Next(1, 4)}.jpg";
            var assetUri = new Uri($"avares://TerminalSimulation.Avalonia/Assets/{imageName}");
            using var assetStream = AssetLoader.Open(assetUri);
            SplashRoot.Background = new global::Avalonia.Media.ImageBrush
            {
                Source = new global::Avalonia.Media.Imaging.Bitmap(assetStream)
            };
        }

        public void SetLoadingText(string text)
        {
            Dispatcher.UIThread.InvokeAsync(() => {
                LoadingText.Text = text;
            });
        }
    }
}
