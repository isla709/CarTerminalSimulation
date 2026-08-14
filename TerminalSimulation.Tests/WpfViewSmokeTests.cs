using TerminalSimulation.Plugins.XunjieCloud.Views;
using TerminalSimulation.Wpf;
using TerminalSimulation.Wpf.Views;
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
                _ = new ConnectionBar();
                _ = new CommunicationLogView();
                _ = new DataPassthroughView();
                _ = new TextDownlinkView();
                _ = new DevicePropertiesView();
                _ = new PluginGalleryView();
                _ = new MessageAnalyzerView();
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
