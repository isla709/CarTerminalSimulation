#if INTERNAL_XUNJIECLOUD
using TerminalSimulation.Plugins.XunjieCloud.Views;
#endif
using TerminalSimulation.Wpf;
using TerminalSimulation.Wpf.Views;
using System.Windows;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class WpfViewSmokeTests
{
    [Fact]
    public void HostAndPluginViews_CanBeConstructedInOneApplicationLifetime()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var mainWindow = new MainWindow();
                var updateCard = (FrameworkElement)mainWindow.FindName("UpdateNotificationCard");
                updateCard.DataContext = new { UpdateNotificationVersion = "preview11-build.20261014.1", IsCheckingForUpdates = false };
                updateCard.Measure(new Size(354, 300));
                updateCard.Arrange(new Rect(updateCard.DesiredSize));
                updateCard.UpdateLayout();
                Assert.InRange(updateCard.ActualHeight, 100, 250);
                var updateBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(updateCard.DesiredSize.Width), (int)Math.Ceiling(updateCard.DesiredSize.Height), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                updateBitmap.Render(updateCard);
                if (Environment.GetEnvironmentVariable("CTS_QA_OUTPUT") is { Length: > 0 } output)
                {
                    Directory.CreateDirectory(output);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(updateBitmap));
                    using var stream = File.Create(Path.Combine(output, "update-bubble.png"));
                    encoder.Save(stream);
                }
                var splash = new SplashWindow();
                var splashFrame = (System.Windows.Controls.Border)splash.Content;
                var splashSurface = (System.Windows.Controls.Grid)splashFrame.Child;
                foreach (var size in new[] { new Size(960, 540), new Size(480, 270) })
                {
                    splashFrame.Measure(size);
                    splashFrame.Arrange(new Rect(size));
                    splashFrame.UpdateLayout();
                    var clip = Assert.IsType<System.Windows.Media.RectangleGeometry>(splashSurface.Clip);
                    Assert.Equal(splashSurface.RenderSize.Width, clip.Rect.Width);
                    Assert.Equal(splashSurface.RenderSize.Height, clip.Rect.Height);
                    Assert.Equal(17, clip.RadiusX);
                    var width = (int)splashSurface.RenderSize.Width;
                    var height = (int)splashSurface.RenderSize.Height;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(splashSurface);
                    var pixels = new byte[width * height * 4];
                    bitmap.CopyPixels(pixels, width * 4, 0);
                    foreach (var index in new[] { 0, width - 1, (height - 1) * width, height * width - 1 })
                        Assert.Equal(0, pixels[index * 4 + 3]);
                    Assert.Equal(255, pixels[((height / 2) * width + width / 2) * 4 + 3]);
                }
                _ = new ThemeSettingsWindow();
                var updateSettings = new ThemeSettingsWindow(openVersions: true);
                Assert.Equal(1, ((System.Windows.Controls.TabControl)updateSettings.FindName("SettingsTabControl")).SelectedIndex);
                _ = new AboutSettingsControl();
#if INTERNAL_XUNJIECLOUD
                _ = new XunjieCloudStreamView();
                // Opening the utility picker again can create a second plugin tab.
                // Keep this as a regression guard for duplicate native-video views.
                _ = new XunjieCloudStreamView();
#endif
                _ = new ConnectionBar();
                _ = new CommunicationLogView();
                _ = new DataPassthroughView();
                _ = new TextDownlinkView();
                _ = new DevicePropertiesView();
                _ = new PluginGalleryView();
                _ = new MessageAnalyzerView();
                var compactHttpView = new HttpRequestToolView();
                compactHttpView.Measure(new Size(660, 460));
                compactHttpView.Arrange(new Rect(0, 0, 660, 460));
                compactHttpView.UpdateLayout();

                var wideHttpView = new HttpRequestToolView();
                wideHttpView.Measure(new Size(1600, 900));
                wideHttpView.Arrange(new Rect(0, 0, 1600, 900));
                wideHttpView.UpdateLayout();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF view construction timed out.");
        Assert.Null(failure);
    }
}
