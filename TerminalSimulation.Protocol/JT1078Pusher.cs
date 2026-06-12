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
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private readonly string _simCard;
        private readonly byte _channelNo;
        private readonly string _h264File;
        private readonly double _targetFps;
        private readonly bool _isConstantFps;

        public event Action<string>? OnLog;

        public event Action<string>? OnStatusUpdate;

        public JT1078Pusher(string simCard, byte channelNo, string h264File, double targetFps, bool isConstantFps)
        {
            _simCard = simCard.PadLeft(12, '0');
            _channelNo = channelNo;
            _h264File = h264File;
            _targetFps = targetFps;
            _isConstantFps = isConstantFps;
        }

        public async Task StartAsync(string ip, int port)
        {
            _cts = new CancellationTokenSource();

            if (!File.Exists(_h264File))
            {
                OnLog?.Invoke($"视频裸流文件不存在: {_h264File}");
                return;
            }

            OnLog?.Invoke("正在解析视频文件，这可能需要一点时间...");
            byte[] fileData = await File.ReadAllBytesAsync(_h264File, _cts.Token);
            var nalus = SplitNalus(fileData);
            var frames = GroupNalusIntoFrames(nalus);
            OnLog?.Invoke($"已成功切分并重组裸流，总 NALU 数: {nalus.Count}，总帧数: {frames.Count}");

            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            _stream = _client.GetStream();
            
            OnLog?.Invoke($"已连接到音视频服务器: {ip}:{port}");
            
            _ = Task.Run(() => PushLoop(_cts.Token, frames), _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
        }

        private async Task PushLoop(CancellationToken token, List<VideoFrame> frames)
        {
            try
            {
                var stream = _stream;
                if (stream == null)
                {
                    OnLog?.Invoke("网络连接尚未就绪");
                    return;
                }

                ushort sequence = 0;
                ulong timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                double sleepDelayMs = _targetFps > 0 ? (1000.0 / _targetFps) : 40.0;
                
                ulong lastIFrameTimestamp = timestamp;
                bool hasPreviousIFrame = false;
                ulong lastFrameTimestamp = timestamp;
                bool hasPreviousFrame = false;

                long totalPushedFrames = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                double expectedElapsedMs = 0;

                int burstIFrameCount = 0;
                bool isBursting = true;

                while (!token.IsCancellationRequested)
                {
                    foreach (var frame in frames)
                    {
                        if (token.IsCancellationRequested) break;

                        bool isIFrame = frame.IsIFrame;
                        byte[] frameData = frame.Data;

                        if (isIFrame && isBursting)
                        {
                            burstIFrameCount++;
                            if (burstIFrameCount >= 2)
                            {
                                isBursting = false;
                                sw.Restart();
                                expectedElapsedMs = 0;
                                OnLog?.Invoke("首个 GOP (关键帧组) 极速推送完成，已解决平台 HLS 切片等待 404 问题，现恢复正常流速");
                            }
                        }

                        ushort lastIFrameInterval = 0;
                        ushort lastFrameInterval = 0;

                        if (hasPreviousFrame)
                        {
                            lastFrameInterval = (ushort)(timestamp - lastFrameTimestamp);
                        }
                        else
                        {
                            lastFrameInterval = 0;
                        }

                        if (isIFrame)
                        {
                            if (hasPreviousIFrame)
                            {
                                lastIFrameInterval = (ushort)(timestamp - lastIFrameTimestamp);
                            }
                            else
                            {
                                lastIFrameInterval = 0;
                            }
                            lastIFrameTimestamp = timestamp;
                            hasPreviousIFrame = true;
                        }
                        else
                        {
                            if (hasPreviousIFrame)
                            {
                                lastIFrameInterval = (ushort)(timestamp - lastIFrameTimestamp);
                            }
                            else
                            {
                                lastIFrameInterval = 0;
                            }
                        }

                        lastFrameTimestamp = timestamp;
                        hasPreviousFrame = true;

                        int maxChunkSize = 950;
                        int offset = 0;
                        int remain = frameData.Length;

                        while (remain > 0)
                        {
                            int chunkSize = Math.Min(remain, maxChunkSize);
                            byte[] chunk = new byte[chunkSize];
                            Array.Copy(frameData, offset, chunk, 0, chunkSize);

                            byte subpackageType;
                            if (frameData.Length <= maxChunkSize) subpackageType = 0; // 原子包
                            else if (offset == 0) subpackageType = 1; // 第一包
                            else if (remain == chunkSize) subpackageType = 2; // 最后一包
                            else subpackageType = 3; // 中间包

                            byte label3Byte = (byte)((isIFrame ? 0x00 : 0x10) | subpackageType);
                            
                            // Marker bit M=1 for the last packet of a frame.
                            bool mBit = (subpackageType == 0 || subpackageType == 2);
                            byte label2Byte = (byte)(mBit ? 226 : 98); // 128 + 98 = 226

                            var package = new JT1078Package
                            {
                                Label1 = new JT1078Label1(0x80), // V=2, P=0, X=0, CC=0
                                Label2 = new JT1078Label2(label2Byte),   
                                Label3 = new JT1078Label3(label3Byte),
                                SIM = _simCard,
                                LogicChannelNumber = _channelNo,
                                Timestamp = timestamp,
                                LastIFrameInterval = lastIFrameInterval,
                                LastFrameInterval = lastFrameInterval,
                                SN = sequence++,
                                Bodies = chunk
                            };

                            byte[] data = JT1078Serializer.Serialize(package);
                            await stream.WriteAsync(data, token);

                            offset += chunkSize;
                            remain -= chunkSize;
                        }

                        totalPushedFrames++;
                        if (totalPushedFrames % 10 == 0)
                        {
                            OnStatusUpdate?.Invoke($"推流中... 已推送 {totalPushedFrames} 帧");
                        }

                        // 时间戳以 1000/FPS 递增
                        timestamp += (ulong)sleepDelayMs;

                        if (isBursting)
                        {
                            // 极速模式下无任何延时，全速发包
                            continue;
                        }

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

        private class VideoFrame
        {
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public bool IsIFrame { get; set; }
        }

        private List<VideoFrame> GroupNalusIntoFrames(List<byte[]> nalus)
        {
            var frames = new List<VideoFrame>();
            var pendingMetadata = new List<byte[]>();
            var currentFrameNalus = new List<byte[]>();
            bool isCurrentIFrame = false;

            byte[]? lastSps = null;
            byte[]? lastPps = null;

            Action flushFrame = () =>
            {
                if (currentFrameNalus.Count == 0) return;

                var listToCombine = new List<byte[]>();
                if (isCurrentIFrame)
                {
                    bool hasSps = pendingMetadata.Any(x => (x.Length > 4 ? x[4] & 0x1F : 0) == 7) || currentFrameNalus.Any(x => (x.Length > 4 ? x[4] & 0x1F : 0) == 7);
                    bool hasPps = pendingMetadata.Any(x => (x.Length > 4 ? x[4] & 0x1F : 0) == 8) || currentFrameNalus.Any(x => (x.Length > 4 ? x[4] & 0x1F : 0) == 8);

                    if (!hasSps && lastSps != null) listToCombine.Add(lastSps);
                    if (!hasPps && lastPps != null) listToCombine.Add(lastPps);
                }

                listToCombine.AddRange(pendingMetadata);
                listToCombine.AddRange(currentFrameNalus);

                int totalLength = listToCombine.Sum(x => x.Length);
                byte[] combined = new byte[totalLength];
                int offset = 0;
                foreach (var part in listToCombine)
                {
                    Array.Copy(part, 0, combined, offset, part.Length);
                    offset += part.Length;
                }

                frames.Add(new VideoFrame { Data = combined, IsIFrame = isCurrentIFrame });
                pendingMetadata.Clear();
                currentFrameNalus.Clear();
                isCurrentIFrame = false;
            };

            foreach (var nalu in nalus)
            {
                int naluType = nalu.Length > 4 ? nalu[4] & 0x1F : 0;
                bool isVcl = (naluType == 1 || naluType == 2 || naluType == 3 || naluType == 4 || naluType == 5 || naluType == 19);
                
                bool isFirstSlice = false;
                if (isVcl && nalu.Length > 5)
                {
                    // slice_header() starts with first_mb_in_slice (ue(v))
                    // first_mb_in_slice == 0 is encoded as bit '1'
                    isFirstSlice = (nalu[5] & 0x80) != 0;
                }

                if (currentFrameNalus.Count > 0)
                {
                    if (isFirstSlice || naluType == 6 || naluType == 7 || naluType == 8 || naluType == 9)
                    {
                        flushFrame();
                    }
                }

                if (naluType == 7) // SPS
                {
                    lastSps = nalu;
                    pendingMetadata.Add(nalu);
                }
                else if (naluType == 8) // PPS
                {
                    lastPps = nalu;
                    pendingMetadata.Add(nalu);
                }
                else if (naluType == 6 || naluType == 9) // SEI, AUD
                {
                    pendingMetadata.Add(nalu);
                }
                else if (isVcl)
                {
                    if (naluType == 5) isCurrentIFrame = true;
                    currentFrameNalus.Add(nalu);
                }
                else
                {
                    pendingMetadata.Add(nalu);
                }
            }

            flushFrame();

            return frames;
        }

        private List<byte[]> SplitNalus(byte[] data)
        {
            var nalus = new List<byte[]>();
            int i = 0;
            int lastNaluStart = -1;
            int lastNaluStartCodeLength = 0;

            while (i < data.Length - 2)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x01)
                {
                    // Check if it's 4-byte start code (preceded by 0x00)
                    bool isFourByte = (i > 0 && data[i - 1] == 0x00);
                    int startCodeOffset = isFourByte ? i - 1 : i;
                    int startCodeLen = isFourByte ? 4 : 3;

                    if (lastNaluStart != -1)
                    {
                        int payloadStart = lastNaluStart + lastNaluStartCodeLength;
                        int payloadLength = startCodeOffset - payloadStart;
                        if (payloadLength > 0)
                        {
                            // Create NALU with normalized 4-byte start code: 00 00 00 01 + payload
                            byte[] nalu = new byte[4 + payloadLength];
                            nalu[0] = 0x00;
                            nalu[1] = 0x00;
                            nalu[2] = 0x00;
                            nalu[3] = 0x01;
                            Array.Copy(data, payloadStart, nalu, 4, payloadLength);
                            nalus.Add(nalu);
                        }
                    }

                    lastNaluStart = startCodeOffset;
                    lastNaluStartCodeLength = startCodeLen;
                    i += 3;
                }
                else
                {
                    i++;
                }
            }

            if (lastNaluStart != -1)
            {
                int payloadStart = lastNaluStart + lastNaluStartCodeLength;
                int payloadLength = data.Length - payloadStart;
                if (payloadLength > 0)
                {
                    byte[] nalu = new byte[4 + payloadLength];
                    nalu[0] = 0x00;
                    nalu[1] = 0x00;
                    nalu[2] = 0x00;
                    nalu[3] = 0x01;
                    Array.Copy(data, payloadStart, nalu, 4, payloadLength);
                    nalus.Add(nalu);
                }
            }

            return nalus;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
