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
                var mainTabs = (System.Windows.Controls.TabControl)mainWindow.FindName("MainTabControl");
                Assert.Equal("系统日志", ((System.Windows.Controls.TabItem)mainTabs.Items[0]).Header);
                Assert.Equal("轨迹模拟", ((System.Windows.Controls.TabItem)mainTabs.Items[1]).Header);
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
                var sampleLog = new TerminalSimulation.Wpf.Services.ApplicationLog();
                sampleLog.Write("发送", "RAW: 7E 01 02 7E");
                sampleLog.Write("系统", "配置已加载，服务已就绪。");
                sampleLog.Write("插件", "示例插件已加载。");
                using var logModel = new TerminalSimulation.Wpf.ViewModels.SystemLogViewModel(sampleLog, System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var logView = new CommunicationLogView { DataContext = new LogViewSampleContext {
                    SystemLogs = logModel, AutoScrollLogs = true,
                    ClearLogsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(logModel.Clear)
                } };
                foreach (var size in new[] { new Size(480, 360), new Size(1100, 500) })
                {
                    logView.Width = size.Width; logView.Height = size.Height;
                    logView.Measure(size); logView.Arrange(new Rect(size)); logView.UpdateLayout();
                    var toolbar = (FrameworkElement)logView.FindName("LogToolbar");
                    var list = (FrameworkElement)logView.FindName("LogListBox");
                    Assert.True(list.TranslatePoint(new Point(), logView).Y >= toolbar.ActualHeight);
                    if (Environment.GetEnvironmentVariable("CTS_QA_OUTPUT") is { Length: > 0 } logOutput)
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        // The view inherits its real host card's background. Give
                        // off-screen screenshots an opaque light host as well.
                        var drawing = new System.Windows.Media.DrawingVisual();
                        using (var context = drawing.RenderOpen())
                        {
                            context.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(size));
                            context.DrawRectangle(new System.Windows.Media.VisualBrush(logView) { AutoLayoutContent = false }, null, new Rect(size));
                        }
                        bitmap.Render(drawing);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        Directory.CreateDirectory(logOutput);
                        using var stream = File.Create(Path.Combine(logOutput, $"system-log-{size.Width}.png"));
                        encoder.Save(stream);
                    }
                }
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

                using (var fixture = new SetupPackageTests())
                {
                    var installer = new TerminalSimulation.Updater.InstallerWindow(fixture.BuildTestPackage());
                    var content = (FrameworkElement)installer.Content;
                    foreach (var dpi in new[] { 96d, 144d, 192d })
                    {
                        var size = new Size(660, 500); content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                        var button = (FrameworkElement)installer.FindName("InstallButton");
                        var heading = (FrameworkElement)installer.FindName("Heading");
                        Assert.True(button.TranslatePoint(new Point(), content).Y > heading.ActualHeight);
                        Assert.True(button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), content).Y <= size.Height + 1);
                        if (Environment.GetEnvironmentVariable("CTS_QA_OUTPUT") is { Length: > 0 } installerOutput)
                        {
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(size.Width * dpi / 96), (int)(size.Height * dpi / 96), dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
                            var drawing = new System.Windows.Media.DrawingVisual(); using (var context = drawing.RenderOpen())
                            { context.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(size)); context.DrawRectangle(new System.Windows.Media.VisualBrush(content) { AutoLayoutContent = false }, null, new Rect(size)); }
                            bitmap.Render(drawing); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            Directory.CreateDirectory(installerOutput); using var stream = File.Create(Path.Combine(installerOutput, $"installer-{dpi}.png")); encoder.Save(stream);
                        }
                    }
                }
                var updater = new TerminalSimulation.Updater.UpdaterWindow();
                var updaterLayout = (FrameworkElement)updater.Content;
                var status = (System.Windows.Controls.ScrollViewer)updater.FindName("StatusScroll");
                var close = (System.Windows.Controls.Button)updater.FindName("CloseButton");
                Assert.Null(updater.FindName("PhaseSubtitle"));
                Assert.Null(updater.FindName("FooterText"));
                var ring = (System.Windows.Controls.Grid)updater.FindName("UpdateRing");
                var arrow = (System.Windows.Shapes.Path)updater.FindName("UpdateArrow");
                var icon = (System.Windows.Controls.Grid)updater.FindName("UpdateIcon");
                Assert.Same(icon, System.Windows.Media.VisualTreeHelper.GetParent(ring));
                Assert.Same(icon, System.Windows.Media.VisualTreeHelper.GetParent(arrow));
                Assert.True(icon.RenderTransform.Value.IsIdentity);
                Assert.True(arrow.RenderTransform.Value.IsIdentity);
                var rotation = Assert.IsType<System.Windows.Media.RotateTransform>(ring.RenderTransform);
                rotation.Angle = 135;
                Assert.True(arrow.RenderTransform.Value.IsIdentity);
                rotation.Angle = 0;
                // Render client content without an unshown Window remeasuring it
                // using testhost's (different) native non-client DPI metrics.
                updaterLayout.Resources = updater.Resources;
                updaterLayout.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, updater.FontFamily);
                updater.Content = null;
                updaterLayout.Margin = new Thickness(0);
                updaterLayout.Width = 644;
                updaterLayout.Height = 462;
                foreach (var scenario in new[] { "download", "error", "success" })
                {
                    if (scenario == "error") updater.ReportCompleted(new TerminalSimulation.Updater.UpdateResult(false,
                        "更新失败：运行时文件正被占用，已恢复原文件。\n" + string.Concat(Enumerable.Repeat("请关闭程序后重试，详细信息见 update.log。", 12))));
                    else if (scenario == "success") updater.ReportCompleted(new TerminalSimulation.Updater.UpdateResult(true, "已完成版本切换，目标程序已启动。"));
                    else updater.ReportProgress(new TerminalSimulation.Updater.UpdateProgress(
                        "正在下载更新包… 59.5 MB", 44, "preview8-build.20260924.2", TerminalSimulation.Updater.UpdateStage.Downloading));
                    updaterLayout.Measure(new Size(644, 462));
                    updaterLayout.Arrange(new Rect(0, 0, 644, 462));
                    updaterLayout.UpdateLayout();
                    Assert.True(status.ViewportHeight >= 60, $"Updater status viewport={status.ViewportHeight:F1}, actual={status.ActualHeight:F1}, root={updaterLayout.ActualHeight:F1} DIP.");
                    var buttonBounds = close.TransformToAncestor(updaterLayout).TransformBounds(new Rect(close.RenderSize));
                    Assert.True(buttonBounds.Bottom <= updaterLayout.ActualHeight + .1);
                    Assert.Equal(scenario != "success", close.IsEnabled);
                    foreach (var dpi in new[] { 96d, 144d, 192d })
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(644 * dpi / 96), (int)(462 * dpi / 96), dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
                        var drawing = new System.Windows.Media.DrawingVisual();
                        using (var context = drawing.RenderOpen())
                            context.DrawRectangle(new System.Windows.Media.VisualBrush(updaterLayout) { AutoLayoutContent = false }, null, new Rect(updaterLayout.RenderSize));
                        bitmap.Render(drawing);
                        if (Environment.GetEnvironmentVariable("CTS_QA_OUTPUT") is { Length: > 0 } updaterOutput)
                        {
                            Directory.CreateDirectory(updaterOutput);
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(updaterOutput, $"updater-{scenario}-{dpi}.png"));
                            encoder.Save(stream);
                        }
                    }
                }
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

    public sealed class LogViewSampleContext
    {
        public TerminalSimulation.Wpf.ViewModels.SystemLogViewModel SystemLogs { get; init; } = null!;
        public bool AutoScrollLogs { get; set; } = true;
        public System.Windows.Input.ICommand ClearLogsCommand { get; init; } = null!;
    }
}
