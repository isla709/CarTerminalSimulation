using System.Net;
using System.Net.Sockets;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.MessageBody;
using TerminalSimulation.Network;
using TerminalSimulation.Protocol;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class SimulatedPlatformIntegrationTests
{
    [Fact]
    public async Task TerminalSession_RegisterAuthHeartbeatAutoReplyAndReconnect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var protocol = new JT808Manager();
        await using var terminal = new TerminalNetworkClient();
        var autoReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.OnDataReceived += data =>
        {
            var package = protocol.Deserialize(data);
            if (package.Header.MsgId != 0x8107) return;
            _ = Task.Run(async () =>
            {
                var reply = new JT808Package
                {
                    Header = new JT808Header { MsgId = 0x0107, TerminalPhoneNo = "13800138000", MsgNum = 9 },
                    Bodies = new JT808_0x0107
                    {
                        TerminalType = 0, MakerId = "TEST ", TerminalModel = "MODEL", TerminalId = "TERM001",
                        Terminal_SIM_ICCID = "89860000000000000000", Terminal_Hardware_Version_Num = "1.0",
                        Terminal_Firmware_Version_Num = "1.0", GNSSModule = 1, CommunicationModule = 1
                    }
                };
                await terminal.SendAsync(protocol.Serialize<JT808_0x0107>(reply));
                autoReply.TrySetResult();
            });
        };

        var accept = listener.AcceptTcpClientAsync();
        await terminal.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var platform = await accept;

        await terminal.SendAsync(protocol.Serialize<JT808_0x0100>(new JT808Package
        {
            Header = new JT808Header { MsgId = 0x0100, TerminalPhoneNo = "13800138000", MsgNum = 1 },
            Bodies = new JT808_0x0100 { AreaID = 11, CityOrCountyId = 1101, MakerId = "TEST ", TerminalModel = "MODEL", TerminalId = "TERM001", PlateColor = 1, PlateNo = "京A00001" }
        }));
        Assert.Equal((ushort)0x0100, protocol.Deserialize(await ReadFrameAsync(platform.GetStream())).Header.MsgId);

        await terminal.SendAsync(protocol.Serialize<JT808_0x0102>(new JT808Package
        {
            Header = new JT808Header { MsgId = 0x0102, TerminalPhoneNo = "13800138000", MsgNum = 2 },
            Bodies = new JT808_0x0102 { Code = "AUTH", IMEI = "123456789012345", SoftwareVersion = "1.0" }
        }));
        Assert.Equal((ushort)0x0102, protocol.Deserialize(await ReadFrameAsync(platform.GetStream())).Header.MsgId);

        await terminal.SendAsync(protocol.Serialize(new JT808Package { Header = new JT808Header { MsgId = 0x0002, TerminalPhoneNo = "13800138000", MsgNum = 3 } }));
        Assert.Equal((ushort)0x0002, protocol.Deserialize(await ReadFrameAsync(platform.GetStream())).Header.MsgId);

        await platform.GetStream().WriteAsync(protocol.Serialize(new JT808Package { Header = new JT808Header { MsgId = 0x8107, TerminalPhoneNo = "13800138000", MsgNum = 8 } }));
        var replyFrame = await ReadFrameAsync(platform.GetStream());
        await autoReply.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal((ushort)0x0107, protocol.Deserialize(replyFrame).Header.MsgId);

        await terminal.DisconnectAsync();
        var reconnectAccept = listener.AcceptTcpClientAsync();
        await terminal.ConnectAsync(IPAddress.Loopback.ToString(), port);
        using var reconnectedPlatform = await reconnectAccept;
        Assert.True(terminal.IsConnected);
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var bytes = new List<byte>();
        var started = false;
        var one = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(one, timeout.Token);
            if (one[0] == 0x7E)
            {
                if (started && bytes.Count > 1) { bytes.Add(0x7E); return bytes.ToArray(); }
                started = true;
                bytes.Clear();
                bytes.Add(0x7E);
            }
            else if (started) bytes.Add(one[0]);
        }
    }
}
