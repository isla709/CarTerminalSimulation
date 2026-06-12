using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class VideoChannelItem : ObservableObject, IDisposable
    {
        [ObservableProperty] private byte _logicalChannelNo;
        [ObservableProperty] private string _videoFilePath = "";
        [ObservableProperty] private bool _isStreaming = false;
        [ObservableProperty] private bool _isTranscoding = false;
        
        [ObservableProperty] private bool _isConstantFramerate = true; // true = 匀速推送, false = 原始帧率
        [ObservableProperty] private double _targetFps = 25.0;

        [ObservableProperty] private string _statusText = "空闲";
        [ObservableProperty] private string _h264FilePath = "";

        private LibVLC _libVLC;
        [ObservableProperty] private MediaPlayer _mediaPlayer;

        private readonly Action<string, string> _logger;

        public VideoChannelItem(byte logicalChannelNo, Action<string, string> logger = null)
        {
            LogicalChannelNo = logicalChannelNo;
            _logger = logger;
            _libVLC = new LibVLC(enableDebugLogs: false);
            _mediaPlayer = new MediaPlayer(_libVLC);
        }

        [RelayCommand]
        private async Task SelectVideoFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.h264;*.avi;*.mkv|所有文件|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                VideoFilePath = dlg.FileName;
                StatusText = "文件已选择，准备处理";

                await ProcessVideoFileAsync();
            }
        }

        private async Task ProcessVideoFileAsync()
        {
            try
            {
                IsTranscoding = true;
                StatusText = "正在解析/转码...";

                if (Path.GetExtension(VideoFilePath).Equals(".h264", StringComparison.OrdinalIgnoreCase))
                {
                    H264FilePath = VideoFilePath;
                    TargetFps = 25.0; // 默认给25
                    StatusText = "H.264 原生流，已就绪";
                }
                else
                {
                    // Check and download FFmpeg
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
                    if (!Directory.Exists(ffmpegPath) || !File.Exists(Path.Combine(ffmpegPath, "ffmpeg.exe")))
                    {
                        StatusText = "正在下载 FFmpeg 转码组件...";
                        Directory.CreateDirectory(ffmpegPath);
                        FFmpeg.SetExecutablesPath(ffmpegPath);
                        var progress = new Progress<ProgressInfo>(p => 
                        {
                            Application.Current.Dispatcher.Invoke(() => 
                            {
                                double downloaded = p.DownloadedBytes / 1024.0 / 1024.0;
                                double total = p.TotalBytes / 1024.0 / 1024.0;
                                StatusText = $"正在下载 FFmpeg 转码组件... {downloaded:F1}MB / {total:F1}MB";
                            });
                        });
                        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath, progress);
                    }
                    else
                    {
                        FFmpeg.SetExecutablesPath(ffmpegPath);
                    }

                    // Get FPS
                    var mediaInfo = await FFmpeg.GetMediaInfo(VideoFilePath);
                    var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
                    if (videoStream != null && videoStream.Framerate > 0)
                    {
                        // 自动平均帧率最后取整数发送
                        TargetFps = Math.Round(videoStream.Framerate);
                    }

                    // Transcode to h264
                    string h264Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "h264");
                    Directory.CreateDirectory(h264Dir);
                    string outputH264 = Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.h264");

                    if (File.Exists(outputH264))
                    {
                        File.Delete(outputH264);
                    }

                    StatusText = "正在转码为 H.264 裸流...";
                    
                    var conversion = FFmpeg.Conversions.New()
                        .AddParameter($"-i \"{VideoFilePath}\" -c:v libx264 -preset ultrafast -tune zerolatency -an -r {(int)TargetFps} -f h264 \"{outputH264}\"");

                    await conversion.Start();

                    H264FilePath = outputH264;
                    StatusText = $"转码完成就绪 ({TargetFps:F1} FPS)";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"处理失败: {ex.Message}";
                _logger?.Invoke("异常", $"通道 {LogicalChannelNo} 视频处理失败: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                IsTranscoding = false;
            }
        }

        private TerminalSimulation.Protocol.JT1078Pusher _pusher;

        public void StartPushing(string ip, int port, string simCard)
        {
            if (string.IsNullOrEmpty(H264FilePath))
            {
                StatusText = "无有效 H.264 视频源，无法推流";
                return;
            }

            StopPushing();

            _pusher = new TerminalSimulation.Protocol.JT1078Pusher(simCard, LogicalChannelNo, H264FilePath, TargetFps, IsConstantFramerate);
            _pusher.OnLog += msg => 
            {
                _logger?.Invoke("音视频", $"通道 {LogicalChannelNo}: {msg}");
            };
            _pusher.OnStatusUpdate += msg =>
            {
                Application.Current.Dispatcher.Invoke(() => 
                {
                    StatusText = msg;
                });
            };

            _ = _pusher.StartAsync(ip, port);
            IsStreaming = true;

            // 播放视频并设置循环
            Application.Current.Dispatcher.Invoke(() => 
            {
                if (!string.IsNullOrEmpty(VideoFilePath) && File.Exists(VideoFilePath))
                {
                    var media = new Media(_libVLC, VideoFilePath, FromType.FromPath);
                    media.AddOption(":input-repeat=65535"); // 无限循环
                    MediaPlayer.Play(media);
                }
            });
        }

        public void StopPushing()
        {
            if (_pusher != null)
            {
                _pusher.Stop();
                _pusher = null;
            }
            IsStreaming = false;
            StatusText = "已停止推流";

            Application.Current.Dispatcher.Invoke(() => 
            {
                MediaPlayer?.Stop();
            });
        }

        public void Dispose()
        {
            StopPushing();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }
    }
}
