using TerminalSimulation.Plugins.XunjieCloud.Views;
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
                _ = new MainWindow();
                _ = new XunjieCloudStreamView();
                // Opening the utility picker again can create a second plugin tab.
                // Keep this as a regression guard for duplicate native-video views.
                _ = new XunjieCloudStreamView();
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
