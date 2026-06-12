using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalSimulation.Network
{
    public class TerminalNetworkClient : IDisposable
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _receiveCts;
        private readonly object _disconnectLock = new object();

        public event Action<byte[]>? OnDataReceived;
        public event Action? OnDisconnected;

        public bool IsConnected => _tcpClient?.Connected == true;

        public async Task ConnectAsync(string ip, int port)
        {
            Disconnect();

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(ip, port);
            _networkStream = _tcpClient.GetStream();

            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);
        }

        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new InvalidOperationException("Not connected to server.");
            }

            await _networkStream.WriteAsync(data, 0, data.Length);
            await _networkStream.FlushAsync();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            if (_networkStream == null) return;

            byte[] buffer = new byte[4096];
            List<byte> currentPacket = new List<byte>();
            bool isReadingPacket = false;

            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        break; // Connection closed
                    }

                    for (int i = 0; i < bytesRead; i++)
                    {
                        byte b = buffer[i];

                        if (b == 0x7E)
                        {
                            if (!isReadingPacket)
                            {
                                // Start of packet
                                isReadingPacket = true;
                                currentPacket.Clear();
                                currentPacket.Add(b);
                            }
                            else if (currentPacket.Count > 1)
                            {
                                // End of packet
                                currentPacket.Add(b);
                                var packetData = currentPacket.ToArray();
                                OnDataReceived?.Invoke(packetData);
                                
                                // Shared delimiter: the end of this packet is also the start of next packet
                                currentPacket.Clear();
                                currentPacket.Add(b);
                                isReadingPacket = true;
                            }
                        }
                        else if (isReadingPacket)
                        {
                            currentPacket.Add(b);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is OperationCanceledException || ex is System.IO.IOException)
            {
                // Expected exceptions on disconnect
            }
            finally
            {
                Disconnect();
                OnDisconnected?.Invoke();
            }
        }

        public void Disconnect()
        {
            lock (_disconnectLock)
            {
                if (_receiveCts != null)
                {
                    try { _receiveCts.Cancel(); } catch { }
                    try { _receiveCts.Dispose(); } catch { }
                    _receiveCts = null;
                }

                if (_networkStream != null)
                {
                    try { _networkStream.Dispose(); } catch { }
                    _networkStream = null;
                }

                if (_tcpClient != null)
                {
                    try { _tcpClient.Dispose(); } catch { }
                    _tcpClient = null;
                }
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
