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
    public class JT1078Pusher : IDisposable, IAsyncDisposable
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private Task? _pushTask;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly string _simCard;
        private readonly byte _channelNo;
        private readonly string _h264File;
        private readonly double _targetFps;
        private readonly bool _isConstantFps;

        public event Action<string>? OnLog;

        public event Action<string>? OnStatusUpdate;
        
        public event Action? OnDisconnected;
        public event Action<Exception>? OnError;

        public long TotalPushedBytes { get; private set; } = 0;

        private readonly string _audioFile;
        private readonly int _dataType;
        private readonly int _audioCodec;

        public JT1078Pusher(string simCard, byte channelNo, string h264File, string audioFile, int dataType, int audioCodec, double targetFps, bool isConstantFps)
        {
            _simCard = simCard.PadLeft(12, '0');
            _channelNo = channelNo;
            _h264File = h264File;
            _audioFile = audioFile ?? throw new ArgumentNullException(nameof(audioFile));
            _dataType = dataType;
            _audioCodec = audioCodec;
            _targetFps = targetFps;
            _isConstantFps = isConstantFps;
        }

        public async Task StartAsync(string ip, int port, CancellationToken cancellationToken = default)
        {
            await StopAsync().ConfigureAwait(false);
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            List<VideoFrame> frames = new List<VideoFrame>();
            if (_dataType == 0 || _dataType == 1)
            {
                if (!File.Exists(_h264File))
                {
                    OnLog?.Invoke($"视频裸流文件不存在: {_h264File}");
                    throw new FileNotFoundException("Video elementary stream does not exist.", _h264File);
                }

                OnLog?.Invoke("正在解析视频文件，这可能需要一点时间...");
                byte[] fileData = await File.ReadAllBytesAsync(_h264File, _cts.Token);
                var nalus = SplitNalus(fileData);
                frames = GroupNalusIntoFrames(nalus);
                OnLog?.Invoke($"已成功切分并重组裸流，总 NALU 数: {nalus.Count}，总帧数: {frames.Count}");
            }

            byte[]? audioData = null;
            if ((_dataType == 0 || _dataType == 2 || _dataType == 3) && !string.IsNullOrEmpty(_audioFile) && File.Exists(_audioFile))
            {
                audioData = await File.ReadAllBytesAsync(_audioFile, _cts.Token);
                OnLog?.Invoke($"已加载音频数据: {audioData.Length} 字节");
            }

            if (_dataType == 2 || _dataType == 3)
            {
                if (audioData == null || audioData.Length == 0)
                {
                    OnLog?.Invoke("纯音频模式但未提供音频文件，中止推流。");
                    throw new InvalidOperationException("Audio-only streaming requires a valid audio file.");
                }
            }

            _client = new TcpClient();
            await _client.ConnectAsync(ip, port, _cts.Token);
            _stream = _client.GetStream();
            
            OnLog?.Invoke($"已连接到音视频服务器: {ip}:{port}");
            
            if (_dataType == 2 || _dataType == 3)
            {
                _pushTask = PushAudioOnlyLoop(_cts.Token, audioData!);
            }
            else
            {
                _pushTask = PushLoop(_cts.Token, frames, audioData);
            }
            }
            catch
            {
                _stream?.Dispose();
                _client?.Dispose();
                _stream = null;
                _client = null;
                _cts?.Dispose();
                _cts = null;
                throw;
            }
            finally { _lifecycleLock.Release(); }
        }

        public async Task StopAsync()
        {
            Task? pushTask;
            CancellationTokenSource? cts;
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                pushTask = _pushTask;
                cts = _cts;
                _pushTask = null;
                _cts = null;
                cts?.Cancel();
                _stream?.Dispose();
                _client?.Dispose();
                _stream = null;
                _client = null;
            }
            finally { _lifecycleLock.Release(); }

            if (pushTask != null)
            {
                try { await pushTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            cts?.Dispose();
        }

        public void Stop() => StopAsync().GetAwaiter().GetResult();

        private async Task PushLoop(CancellationToken token, List<VideoFrame> frames, byte[]? audioData)
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
                double exactTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // 恢复为绝对系统时间，防止新平台在绝对时间对齐上出错
                ulong timestamp = (ulong)exactTimestamp;
                double sleepDelayMs = _targetFps > 0 ? (1000.0 / _targetFps) : 40.0;
                
                ulong lastIFrameTimestamp = timestamp;
                bool hasPreviousIFrame = false;
                ulong lastFrameTimestamp = timestamp;
                bool hasPreviousFrame = false;

                ulong lastAudioTimestamp = timestamp;
                int audioOffset = 0;

                long totalPushedFrames = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                double expectedElapsedMs = 0;

                while (!token.IsCancellationRequested)
                {
                    foreach (var frame in frames)
                    {
                        if (token.IsCancellationRequested) break;

                        bool isIFrame = frame.IsIFrame;
                        byte[] frameData = frame.Data;

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

                        using var framePayloadStream = new MemoryStream(frameData.Length + 1024);

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
                            framePayloadStream.Write(data, 0, data.Length);

                            offset += chunkSize;
                            remain -= chunkSize;
                        }

                        byte[] finalFrameData = framePayloadStream.ToArray();
                        await stream.WriteAsync(finalFrameData, token);
                        TotalPushedBytes += finalFrameData.Length;

                        // ---- 穿插推送音频数据 (如果有) ----
                        if (audioData != null && audioData.Length > 0)
                        {
                            long msElapsedForAudio = (long)(timestamp - lastAudioTimestamp);
                            
                            if (_audioCodec == 0) // G.711A
                            {
                                if (msElapsedForAudio >= 40) // 积攒40ms以上的音频数据再发送
                                {
                                    int bytesToPush = (int)(msElapsedForAudio * 8); // 8000Hz 8-bit mono = 8 bytes/ms
                                    
                                    while (bytesToPush > 0)
                                    {
                                        int chunkLen = Math.Min(320, bytesToPush); // 每包最多320字节(40ms)
                                        if (chunkLen > audioData.Length) chunkLen = audioData.Length;
                                        
                                        byte[] audioChunk = new byte[chunkLen];
                                        for(int i = 0; i < chunkLen; i++)
                                        {
                                            audioChunk[i] = audioData[(audioOffset + i) % audioData.Length];
                                        }
                                        audioOffset = (audioOffset + chunkLen) % audioData.Length;
                                        bytesToPush -= chunkLen;

                                        var audioPackage = new JT1078Package
                                        {
                                            Label1 = new JT1078Label1(0x80), // V=2, P=0, X=0, CC=0
                                            Label2 = new JT1078Label2(134),  // M=1(128) + PT=6(G.711A in JT1078)
                                            Label3 = new JT1078Label3(0x30), // dataType=3 (音频)
                                            SIM = _simCard,
                                            LogicChannelNumber = _channelNo,
                                            Timestamp = lastAudioTimestamp,
                                            LastIFrameInterval = 0,
                                            LastFrameInterval = 0,
                                            SN = sequence++,
                                            Bodies = audioChunk
                                        };

                                        byte[] aData = JT1078Serializer.Serialize(audioPackage);
                                        await stream.WriteAsync(aData, token);
                                        TotalPushedBytes += aData.Length;
                                        
                                        lastAudioTimestamp += (ulong)(chunkLen / 8);
                                    }
                                }
                            }
                            else if (_audioCodec == 1) // AAC
                            {
                                while (true)
                                {
                                    if (audioOffset + 7 > audioData.Length) audioOffset = 0; // wrap around
                                    
                                    bool foundSync = false;
                                    while (audioOffset + 7 <= audioData.Length)
                                    {
                                        if (audioData[audioOffset] == 0xFF && (audioData[audioOffset + 1] & 0xF0) == 0xF0)
                                        {
                                            foundSync = true;
                                            break;
                                        }
                                        audioOffset++;
                                    }

                                    if (!foundSync) 
                                    {
                                        audioOffset = 0;
                                        break;
                                    }

                                    int sampleRateIndex = (audioData[audioOffset + 2] & 0x3C) >> 2;
                                    int[] sampleRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };
                                    int sampleRate = sampleRateIndex < sampleRates.Length ? sampleRates[sampleRateIndex] : 8000;
                                    double frameDurationMs = 1024.0 * 1000.0 / sampleRate;

                                    if ((long)(timestamp - lastAudioTimestamp) < (long)frameDurationMs)
                                    {
                                        break;
                                    }

                                    int frameLength = ((audioData[audioOffset + 3] & 0x03) << 11) | 
                                                      (audioData[audioOffset + 4] << 3) | 
                                                      ((audioData[audioOffset + 5] & 0xE0) >> 5);

                                    if (frameLength > 0 && audioOffset + frameLength <= audioData.Length)
                                    {
                                        byte[] audioChunk = new byte[frameLength];
                                        Array.Copy(audioData, audioOffset, audioChunk, 0, frameLength);
                                        audioOffset = (audioOffset + frameLength) % audioData.Length;

                                        var audioPackage = new JT1078Package
                                        {
                                            Label1 = new JT1078Label1(0x80),
                                            Label2 = new JT1078Label2(147),  // M=1(128) + PT=19(AAC)
                                            Label3 = new JT1078Label3(0x30),
                                            SIM = _simCard,
                                            LogicChannelNumber = _channelNo,
                                            Timestamp = lastAudioTimestamp,
                                            LastIFrameInterval = 0,
                                            LastFrameInterval = 0,
                                            SN = sequence++,
                                            Bodies = audioChunk
                                        };

                                        byte[] aData = JT1078Serializer.Serialize(audioPackage);
                                        await stream.WriteAsync(aData, token);
                                        TotalPushedBytes += aData.Length;
                                    }
                                    else
                                    {
                                        audioOffset = 0; // skip broken frame or end of file
                                    }
                                    
                                    lastAudioTimestamp += (ulong)frameDurationMs;
                                }
                            }
                        }
                        // ------------------------------------

                        totalPushedFrames++;
                        if (totalPushedFrames % 10 == 0)
                        {
                            OnStatusUpdate?.Invoke($"推流中... 已推送 {totalPushedFrames} 帧");
                        }

                        // 时间戳精确递增
                        exactTimestamp += sleepDelayMs;
                        timestamp = (ulong)exactTimestamp;

                        if (_isConstantFps)
                        {
                            expectedElapsedMs += sleepDelayMs;
                            long actualElapsed = sw.ElapsedMilliseconds;

                            if (expectedElapsedMs > actualElapsed)
                            {
                                int delay = (int)(expectedElapsedMs - actualElapsed);
                                if (delay > 0)
                                {
                                    await Task.Delay(delay, token);
                                }
                                
                                // 剩余的时间用自旋等待，保证 60fps 这种高频调用的精确度
                                System.Threading.SpinWait.SpinUntil(() => sw.ElapsedMilliseconds >= expectedElapsedMs || token.IsCancellationRequested);
                            }
                            else if (actualElapsed - expectedElapsedMs > 2000)
                            {
                                // 落后超过2秒，说明网络拥堵或IO阻塞
                                // 重置时间轴并同步跳过时间戳，防止播放端收到滞后时间戳而疯狂快进/抖动
                                double skipMs = actualElapsed - expectedElapsedMs;
                                expectedElapsedMs = actualElapsed;
                                exactTimestamp += skipMs;
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
                OnError?.Invoke(ex);
            }
            finally
            {
                OnDisconnected?.Invoke();
            }
        }

        private async Task PushAudioOnlyLoop(CancellationToken token, byte[] audioData)
        {
            try
            {
                var stream = _stream;
                if (stream == null || audioData == null || audioData.Length == 0) return;

                ushort sequence = 0;
                double exactTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                ulong timestamp = (ulong)exactTimestamp;
                
                int audioOffset = 0;
                long totalPushedPackets = 0;
                
                var sw = System.Diagnostics.Stopwatch.StartNew();
                double expectedElapsedMs = 0;

                while (!token.IsCancellationRequested)
                {
                    if (_audioCodec == 0) // G.711A
                    {
                        int chunkLen = 320; // 40ms per packet
                        byte[] audioChunk = new byte[chunkLen];
                        for(int i = 0; i < chunkLen; i++)
                        {
                            audioChunk[i] = audioData[(audioOffset + i) % audioData.Length];
                        }
                        audioOffset = (audioOffset + chunkLen) % audioData.Length;

                        var audioPackage = new JT1078Package
                        {
                            Label1 = new JT1078Label1(0x80), // V=2, CC=0
                            Label2 = new JT1078Label2(134),  // M=1, PT=6 (G.711A in JT1078)
                            Label3 = new JT1078Label3(0x30), // dataType=3 (音频)
                            SIM = _simCard,
                            LogicChannelNumber = _channelNo,
                            Timestamp = timestamp,
                            LastIFrameInterval = 0,
                            LastFrameInterval = 0,
                            SN = sequence++,
                            Bodies = audioChunk
                        };

                        byte[] aData = JT1078Serializer.Serialize(audioPackage);
                        await stream.WriteAsync(aData, token);
                        TotalPushedBytes += aData.Length;
                        
                        expectedElapsedMs += 40;
                        timestamp += 40;
                    }
                    else if (_audioCodec == 1) // AAC
                    {
                        if (audioOffset + 7 > audioData.Length) audioOffset = 0;
                        
                        bool foundSync = false;
                        while (audioOffset + 7 <= audioData.Length)
                        {
                            if (audioData[audioOffset] == 0xFF && (audioData[audioOffset + 1] & 0xF0) == 0xF0)
                            {
                                foundSync = true;
                                break;
                            }
                            audioOffset++;
                        }

                        if (!foundSync) audioOffset = 0;

                        int frameLength = 0;
                        int sampleRate = 8000;
                        if (foundSync)
                        {
                            frameLength = ((audioData[audioOffset + 3] & 0x03) << 11) | 
                                          (audioData[audioOffset + 4] << 3) | 
                                          ((audioData[audioOffset + 5] & 0xE0) >> 5);
                                          
                            int sampleRateIndex = (audioData[audioOffset + 2] & 0x3C) >> 2;
                            int[] sampleRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };
                            sampleRate = sampleRateIndex < sampleRates.Length ? sampleRates[sampleRateIndex] : 8000;
                        }

                        if (frameLength > 0 && audioOffset + frameLength <= audioData.Length)
                        {
                            byte[] audioChunk = new byte[frameLength];
                            Array.Copy(audioData, audioOffset, audioChunk, 0, frameLength);
                            audioOffset = (audioOffset + frameLength) % audioData.Length;

                            var audioPackage = new JT1078Package
                            {
                                Label1 = new JT1078Label1(0x80),
                                Label2 = new JT1078Label2(147),  // M=1, PT=19 (AAC)
                                Label3 = new JT1078Label3(0x30),
                                SIM = _simCard,
                                LogicChannelNumber = _channelNo,
                                Timestamp = timestamp,
                                LastIFrameInterval = 0,
                                LastFrameInterval = 0,
                                SN = sequence++,
                                Bodies = audioChunk
                            };

                            byte[] aData = JT1078Serializer.Serialize(audioPackage);
                            await stream.WriteAsync(aData, token);
                            TotalPushedBytes += aData.Length;
                        }
                        else
                        {
                            audioOffset = 0;
                        }
                        
                        double frameDurationMs = 1024.0 * 1000.0 / sampleRate;
                        expectedElapsedMs += frameDurationMs;
                        timestamp += (ulong)frameDurationMs;
                    }

                    totalPushedPackets++;
                    if (totalPushedPackets % 25 == 0)
                    {
                        OnStatusUpdate?.Invoke($"纯音频推流中... 已推送 {totalPushedPackets} 包");
                    }

                    exactTimestamp += 40.0;
                    timestamp = (ulong)exactTimestamp;
                    expectedElapsedMs += 40.0;

                    long actualElapsed = sw.ElapsedMilliseconds;
                    if (expectedElapsedMs > actualElapsed)
                    {
                        int delay = (int)(expectedElapsedMs - actualElapsed);
                        await Task.Delay(delay, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                OnLog?.Invoke("推流已手动中止");
                OnStatusUpdate?.Invoke("推流已手动中止");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"纯音频推流异常: {ex.Message}");
                OnError?.Invoke(ex);
            }
            finally
            {
                OnDisconnected?.Invoke();
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
            _lifecycleLock.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _lifecycleLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
