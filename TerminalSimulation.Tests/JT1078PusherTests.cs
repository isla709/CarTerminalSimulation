using System.Net;
using System.Net.Sockets;
using TerminalSimulation.Protocol;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class JT1078PusherTests
{
    [Fact]
    public async Task StartAsync_MissingVideoFailsWithoutReportingRunningState()
    {
        await using var pusher = new JT1078Pusher("13800138000", 1, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".h264"), string.Empty, 1, 0, 25, true);
        await Assert.ThrowsAsync<FileNotFoundException>(() => pusher.StartAsync(IPAddress.Loopback.ToString(), 1));
    }

    [Fact]
    public async Task StartAsync_AudioOnlyRequiresExistingNonEmptyAudioFile()
    {
        await using var pusher = new JT1078Pusher("13800138000", 1, string.Empty, string.Empty, 2, 0, 25, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.StartAsync(IPAddress.Loopback.ToString(), 1));
    }

    [Fact]
    public void Constructor_RejectsNullAudioPath()
    {
        Assert.Throws<ArgumentNullException>(() => new JT1078Pusher("13800138000", 1, "video.h264", null!, 1, 0, 25, true));
    }

    [Fact]
    public async Task StartAndStopAsync_ThirtyCyclesLeaveNoRunningSession()
    {
        var videoPath = Path.Combine(Path.GetTempPath(), $"jt1078-{Guid.NewGuid():N}.h264");
        await File.WriteAllBytesAsync(videoPath,
        [
            0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1F,
            0, 0, 0, 1, 0x68, 0xCE, 0x06, 0xE2,
            0, 0, 0, 1, 0x65, 0x88, 0x84, 0x21
        ]);
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await using var pusher = new JT1078Pusher("13800138000", 1, videoPath, string.Empty, 1, 0, 25, true);

            for (var iteration = 0; iteration < 30; iteration++)
            {
                var accept = listener.AcceptTcpClientAsync();
                await pusher.StartAsync(IPAddress.Loopback.ToString(), port);
                using var connectedClient = await accept;
                await pusher.StopAsync();
            }
        }
        finally
        {
            File.Delete(videoPath);
        }
    }
}
