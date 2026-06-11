using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.MessageBody;
using TerminalSimulation.Protocol;

class Program
{
    static async Task Main()
    {
        var manager = new JT808Manager();
        var listener = new TcpListener(IPAddress.Any, 8088);
        listener.Start();
        Console.WriteLine("Server started on port 8088...");

        while (true)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync();
                Console.WriteLine("Client connected!");
                _ = HandleClientAsync(client, manager);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }

    static async Task HandleClientAsync(TcpClient client, JT808Manager manager)
    {
        using var stream = client.GetStream();
        var buffer = new byte[4096];
        var currentPacket = new System.Collections.Generic.List<byte>();
        bool isReadingPacket = false;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000);
                if (client.Connected)
                {
                    // 1. Send UTF-8 encoded text downlink
                    Console.WriteLine("Sending UTF-8 text downlink...");
                    manager.Encoding = System.Text.Encoding.UTF8;
                    
                    var packageUtf8 = new JT808Package
                    {
                        Header = new JT808Header
                        {
                            MsgId = 0x8300,
                            TerminalPhoneNo = "13812345678",
                            MsgNum = 1
                        },
                        Bodies = new JT808_0x8300
                        {
                            TextFlag = 4, // TTS
                            TextInfo = "这是UTF8编码的TTS测试"
                        }
                    };

                    byte[] dataUtf8 = manager.Serialize<JT808_0x8300>(packageUtf8, JT808Version.JTT2013);
                    await stream.WriteAsync(dataUtf8, 0, dataUtf8.Length);
                    await stream.FlushAsync();
                    Console.WriteLine("Sent UTF-8 message.");

                    await Task.Delay(6000);

                    if (client.Connected)
                    {
                        // 2. Send GBK encoded text downlink
                        Console.WriteLine("Sending GBK text downlink...");
                        manager.Encoding = System.Text.Encoding.GetEncoding("GBK");
                        
                        var packageGbk = new JT808Package
                        {
                            Header = new JT808Header
                            {
                                MsgId = 0x8300,
                                TerminalPhoneNo = "13812345678",
                                MsgNum = 2
                            },
                            Bodies = new JT808_0x8300
                            {
                                TextFlag = 4, // TTS
                                TextInfo = "这是GBK编码的TTS测试"
                            }
                        };

                        byte[] dataGbk = manager.Serialize<JT808_0x8300>(packageGbk, JT808Version.JTT2013);
                        await stream.WriteAsync(dataGbk, 0, dataGbk.Length);
                        await stream.FlushAsync();
                        Console.WriteLine("Sent GBK message.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending: {ex.Message}");
            }
        });

        try
        {
            while (client.Connected)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    if (b == 0x7E)
                    {
                        if (!isReadingPacket)
                        {
                            isReadingPacket = true;
                            currentPacket.Clear();
                            currentPacket.Add(b);
                        }
                        else
                        {
                            currentPacket.Add(b);
                            byte[] packetBytes = currentPacket.ToArray();
                            isReadingPacket = false;

                            try
                            {
                                var receivedPackage = manager.Deserialize(packetBytes);
                                Console.WriteLine($"Received packet MsgId: 0x{receivedPackage.Header.MsgId:X4}");
                                if (receivedPackage.Header.MsgId == 0x0001 && receivedPackage.Bodies is JT808_0x0001 resp)
                                {
                                    Console.WriteLine($"Response Result: {resp.TerminalResult}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error parsing packet: {ex.Message}");
                            }
                        }
                    }
                    else if (isReadingPacket)
                    {
                        currentPacket.Add(b);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Disconnected: {ex.Message}");
        }
    }
}
