using System.Net;
using System.Net.Sockets;
using TerminalSimulation.Network;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class TerminalNetworkClientTests
{
    [Fact]
    public async Task ReceiveLoop_ReassemblesSplitAndSharedDelimiterFrames()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new TerminalNetworkClient();
        var frames = new List<byte[]>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnDataReceived += frame => { lock (frames) { frames.Add(frame); if (frames.Count == 2) received.TrySetResult(); } };

        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var server = await accept;
        var stream = server.GetStream();
        await stream.WriteAsync(new byte[] { 0x7E, 0x01 });
        await stream.WriteAsync(new byte[] { 0x02, 0x7E, 0x03, 0x7E });
        await received.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(new byte[] { 0x7E, 0x01, 0x02, 0x7E }, frames[0]);
        Assert.Equal(new byte[] { 0x7E, 0x03, 0x7E }, frames[1]);
    }

    [Fact]
    public async Task SendAsync_SerializesConcurrentFramesWithoutInterleaving()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new TerminalNetworkClient();
        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var server = await accept;

        var payloads = Enumerable.Range(0, 50)
            .Select(i => Enumerable.Repeat((byte)i, 256).ToArray()).ToArray();
        await Task.WhenAll(payloads.Select(data => client.SendAsync(data)));

        var expectedLength = payloads.Sum(x => x.Length);
        var all = new byte[expectedLength];
        var offset = 0;
        var stream = server.GetStream();
        while (offset < all.Length)
        {
            offset += await stream.ReadAsync(all.AsMemory(offset));
        }

        for (var block = 0; block < 50; block++)
        {
            var value = all[block * 256];
            Assert.All(all.AsSpan(block * 256, 256).ToArray(), item => Assert.Equal(value, item));
        }
        Assert.Equal(50, all.Chunk(256).Select(chunk => chunk[0]).Distinct().Count());
    }
}
