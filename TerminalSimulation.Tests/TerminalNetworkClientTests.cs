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

    [Fact]
    public async Task ReceiveLoop_RejectsOversizedFrameAndDisconnects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new TerminalNetworkClient();
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnError += exception => error.TrySetResult(exception);
        client.OnDisconnected += () => disconnected.TrySetResult();

        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var server = await accept;
        var oversized = new byte[64 * 1024 + 2];
        oversized[0] = 0x7E;
        Array.Fill(oversized, (byte)0x01, 1, oversized.Length - 1);
        await server.GetStream().WriteAsync(oversized);

        Assert.IsType<InvalidDataException>(await error.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisconnectAsync_WaitsForSessionAndAllowsReconnect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new TerminalNetworkClient();

        var firstAccept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var firstServer = await firstAccept;
        await client.DisconnectAsync();
        Assert.False(client.IsConnected);

        var secondAccept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var secondServer = await secondAccept;
        await client.SendAsync(new byte[] { 0x7E, 0x01, 0x01, 0x7E });
        var received = new byte[4];
        await secondServer.GetStream().ReadExactlyAsync(received);
        Assert.Equal(new byte[] { 0x7E, 0x01, 0x01, 0x7E }, received);
    }

    [Fact]
    public async Task ConnectAndDisconnect_OneHundredCyclesLeaveNoActiveSession()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new TerminalNetworkClient();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var accept = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
            using var server = await accept;
            await client.DisconnectAsync();
            Assert.False(client.IsConnected);
        }
    }

    [Fact]
    public async Task SendAsync_ConcurrentJt808FramesRemainDelimitedAndChecksummed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var client = new TerminalNetworkClient();
        var accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var server = await accept;

        var frames = Enumerable.Range(0, 50).Select(index =>
        {
            var body = new byte[] { (byte)index, (byte)(index >> 8), 0x55, 0x2A };
            var checksum = body.Aggregate((byte)0, (value, item) => (byte)(value ^ item));
            return new byte[] { 0x7E }.Concat(body).Append(checksum).Append((byte)0x7E).ToArray();
        }).ToArray();
        await Task.WhenAll(frames.Select(frame => client.SendAsync(frame)));

        var bytes = new byte[frames.Sum(frame => frame.Length)];
        await server.GetStream().ReadExactlyAsync(bytes);
        var receivedFrames = new List<byte[]>();
        for (var offset = 0; offset < bytes.Length; offset += 7)
        {
            var frame = bytes.AsSpan(offset, 7).ToArray();
            Assert.Equal(0x7E, frame[0]);
            Assert.Equal(0x7E, frame[^1]);
            Assert.Equal(frame[^2], frame.AsSpan(1, 4).ToArray().Aggregate((byte)0, (value, item) => (byte)(value ^ item)));
            receivedFrames.Add(frame);
        }
        Assert.Equal(50, receivedFrames.Count);
    }
}
