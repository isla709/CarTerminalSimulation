using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using JT1078.Protocol;
using JT1078.Protocol.Enums;

namespace TerminalSimulation.Protocol
{
    public class JT1078Pusher : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private readonly string _simCard;
        private readonly byte _channelNo;
        private readonly string _h264File;
        private readonly double _targetFps;
        private readonly bool _isConstantFps;

        public event Action<string> OnLog;

        public event Action<string> OnStatusUpdate;

        public JT1078Pusher(string simCard, byte channelNo, string h264File, double targetFps, bool isConstantFps)
        {
            _simCard = simCard;
            _channelNo = channelNo;
            _h264File = h264File;
            _targetFps = targetFps;
            _isConstantFps = isConstantFps;
        }

        public async Task StartAsync(string ip, int port)
        {
            _cts = new CancellationTokenSource();
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            _stream = _client.GetStream();
            
            OnLog?.Invoke($"已连接到音视频服务器: {ip}:{port}");
            
            _ = Task.Run(() => PushLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
        }

        private async Task PushLoop(CancellationToken token)
        {
            try
            {
                if (!File.Exists(_h264File))
                {
                    OnLog?.Invoke($"视频裸流文件不存在: {_h264File}");
                    return;
                }

                // 读入所有 H.264 字节
                byte[] fileData = await File.ReadAllBytesAsync(_h264File, token);
                var nalus = SplitNalus(fileData);
                OnLog?.Invoke($"已成功切分裸流，总帧数: {nalus.Count}");

                ushort sequence = 0;
                ulong timestamp = 0;
                double sleepDelayMs = _targetFps > 0 ? (1000.0 / _targetFps) : 40.0;
                
                long totalPushedFrames = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                double expectedElapsedMs = 0;

                while (!token.IsCancellationRequested)
                {
                    foreach (var nalu in nalus)
                    {
                        if (token.IsCancellationRequested) break;

                        // I 帧或者 P 帧
                        // NALU Type: start code is 4 bytes (00 00 00 01), so NALU header is at index 4
                        int naluType = nalu.Length > 4 ? nalu[4] & 0x1F : 0;
                        bool isIFrame = naluType == 5 || naluType == 7 || naluType == 8; // IDR, SPS, PPS
                        bool isMetadata = naluType == 6 || naluType == 7 || naluType == 8; // SEI, SPS, PPS

                        int maxChunkSize = 950;
                        int offset = 0;
                        int remain = nalu.Length;

                        while (remain > 0)
                        {
                            int chunkSize = Math.Min(remain, maxChunkSize);
                            byte[] chunk = new byte[chunkSize];
                            Array.Copy(nalu, offset, chunk, 0, chunkSize);

                            byte subpackageType;
                            if (nalu.Length <= maxChunkSize) subpackageType = 0; // 原子包
                            else if (offset == 0) subpackageType = 1; // 第一包
                            else if (remain == chunkSize) subpackageType = 2; // 最后一包
                            else subpackageType = 3; // 中间包

                            byte label3Byte = (byte)((isIFrame ? 0x00 : 0x10) | subpackageType);
                            
                            // Marker bit is MSB of Label2. PT is 98 (0x62).
                            // M=1 for the last packet of a frame. For simplicity, we set M=1 at the end of every NALU except if it's metadata before an IDR.
                            // Actually, setting M=1 for the end of ANY NALU is usually fine, but strictly it should be 1 for the end of the Access Unit.
                            // Let's set M=1 if it's the last packet of the NALU, AND it's not metadata (so M=1 on IDR/P frame end).
                            bool mBit = (subpackageType == 0 || subpackageType == 2) && !isMetadata;
                            byte label2Byte = (byte)(mBit ? 226 : 98); // 128 + 98 = 226

                            var package = new JT1078Package
                            {
                                Label1 = new JT1078Label1(0x81), // V=2, P=0, X=0, CC=1
                                Label2 = new JT1078Label2(label2Byte),   
                                Label3 = new JT1078Label3(label3Byte),
                                SIM = _simCard,
                                LogicChannelNumber = _channelNo,
                                Timestamp = timestamp,
                                LastIFrameInterval = 0,
                                LastFrameInterval = 0,
                                SN = sequence++,
                                Bodies = chunk
                            };

                            byte[] data = JT1078Serializer.Serialize(package);
                            await _stream.WriteAsync(data, token);

                            offset += chunkSize;
                            remain -= chunkSize;
                        }

                        // 如果是 SPS/PPS/SEI 等元数据，不增加时间戳，也不进行延时等待
                        if (!isMetadata)
                        {
                            totalPushedFrames++;
                            if (totalPushedFrames % 10 == 0)
                            {
                                OnStatusUpdate?.Invoke($"推流中... 已推送 {totalPushedFrames} 帧");
                            }

                            // 时间戳以 1000/FPS 递增
                            timestamp += (ulong)sleepDelayMs;

                            if (_isConstantFps)
                            {
                                expectedElapsedMs += sleepDelayMs;
                                long actualElapsed = sw.ElapsedMilliseconds;

                                if (expectedElapsedMs > actualElapsed)
                                {
                                    int delay = (int)(expectedElapsedMs - actualElapsed);
                                    if (delay > 15)
                                    {
                                        // Task.Delay 精度较差，留出缓冲
                                        await Task.Delay(delay - 15, token);
                                    }
                                    
                                    // 剩余的时间用自旋或短暂休眠等待，保证 60fps 这种高频调用的精确度
                                    while (sw.ElapsedMilliseconds < expectedElapsedMs && !token.IsCancellationRequested)
                                    {
                                        System.Threading.Thread.Sleep(1);
                                    }
                                }
                                else if (actualElapsed - expectedElapsedMs > 2000)
                                {
                                    // 落后超过2秒，重置时间轴以防止瞬间大爆发
                                    expectedElapsedMs = actualElapsed;
                                }
                            }
                            else
                            {
                                // 如果是原始帧率，也需要按此间隔发送，否则发送过快
                                await Task.Delay((int)sleepDelayMs, token);
                            }
                        }
                    }
                    
                    OnLog?.Invoke("文件读取到达末尾，开始新一轮循环推送");
                    OnStatusUpdate?.Invoke($"文件到达末尾，重新循环 (总计: {totalPushedFrames} 帧)");
                }
            }
            catch (OperationCanceledException)
            {
                OnLog?.Invoke("推流已手动中止");
                OnStatusUpdate?.Invoke("推流已手动中止");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"推流异常: {ex.Message}");
            }
        }

        private List<byte[]> SplitNalus(byte[] data)
        {
            var nalus = new List<byte[]>();
            int i = 0;
            int lastNaluStart = -1;

            while (i < data.Length - 4)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x00 && data[i + 3] == 0x01)
                {
                    if (lastNaluStart != -1)
                    {
                        int length = i - lastNaluStart;
                        byte[] nalu = new byte[length];
                        Array.Copy(data, lastNaluStart, nalu, 0, length);
                        nalus.Add(nalu);
                    }
                    lastNaluStart = i; // 保留 00 00 00 01 起始码，平台播放器通常需要
                    i += 4;
                }
                else
                {
                    i++;
                }
            }

            if (lastNaluStart != -1 && lastNaluStart < data.Length)
            {
                int length = data.Length - lastNaluStart;
                byte[] nalu = new byte[length];
                Array.Copy(data, lastNaluStart, nalu, 0, length);
                nalus.Add(nalu);
            }

            return nalus;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
