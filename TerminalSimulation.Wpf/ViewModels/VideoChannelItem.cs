using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using TerminalSimulation.Wpf.Services.Media;
using TerminalSimulation.Wpf.Services;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class VideoChannelItem : ObservableObject, IDisposable, IAsyncDisposable
    {
        [ObservableProperty] private byte _logicalChannelNo;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPreviewBtnVisible))]
        [NotifyPropertyChangedFor(nameof(PlaceholderText))]
        private string _videoFilePath = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVideoViewVisible))]
        [NotifyPropertyChangedFor(nameof(IsPreviewBtnVisible))]
        [NotifyPropertyChangedFor(nameof(PlaceholderText))]
        private bool _isStreaming = false;

        [ObservableProperty] private bool _isTranscoding = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVideoViewVisible))]
        [NotifyPropertyChangedFor(nameof(IsPreviewBtnVisible))]
        [NotifyPropertyChangedFor(nameof(PlaceholderText))]
        [NotifyPropertyChangedFor(nameof(PreviewButtonText))]
        private bool _isPreviewing = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasThumbnail))]
        private string _thumbnailPath = "";

        public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MuteIcon))]
        private bool _isMuted = true;

        public string MuteIcon => IsMuted ? "VolumeOff" : "VolumeHigh";
        
        [ObservableProperty] private bool _isConstantFramerate = true; // true = 匀速推送, false = 原始帧率
        [ObservableProperty] private double _targetFps = 25.0;

        [ObservableProperty] private string _statusText = "空闲";
        [ObservableProperty] private string _trafficText = "";
        [ObservableProperty] private string _h264FilePath = "";
        [ObservableProperty] private string _g711aFilePath = "";
        [ObservableProperty] private string _aacFilePath = "";

        private long _lastTotalBytes = 0;
        private DateTime _lastTrafficUpdateTime = DateTime.MinValue;

        private LibVLC? _libVLC;
        [ObservableProperty] private MediaPlayer? _mediaPlayer;

        private readonly Action<string, string>? _logger;
        private readonly IInProcessMediaTranscoder _transcoder = new NativeFfmpegTranscoder();

        public VideoChannelItem(byte logicalChannelNo, Action<string, string>? logger = null)
        {
            LogicalChannelNo = logicalChannelNo;
            _logger = logger;
        }

        private MediaPlayer EnsureMediaPlayer()
        {
            if (MediaPlayer != null) return MediaPlayer;
            _libVLC = new LibVLC(enableDebugLogs: false);
            MediaPlayer = new MediaPlayer(_libVLC);
            return MediaPlayer;
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
                    string h264Dir = Path.Combine(TerminalSimulation.Wpf.Helpers.PathHelper.ExeDir, "h264");
                    Directory.CreateDirectory(h264Dir);
                    string outputH264 = Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.h264");

                    if (File.Exists(outputH264))
                    {
                        File.Delete(outputH264);
                    }

                    var mediaInfo = _transcoder.Probe(VideoFilePath);
                    bool hasAudio = mediaInfo.HasAudio;

                    // 提取原始视频帧率，如果提取失败则默认 25
                    double originalFps = mediaInfo.FrameRate;
                    if (originalFps <= 0) originalFps = 25.0;
                    
                    TargetFps = originalFps;
                    int gop = (int)Math.Max(10, Math.Round(originalFps * 2)); // 2秒一个关键帧

                    string? outputG711a = hasAudio ? Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.g711a") : null;
                    string? outputAac = hasAudio ? Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.aac") : null;
                    if (outputG711a != null && File.Exists(outputG711a)) File.Delete(outputG711a);
                    if (outputAac != null && File.Exists(outputAac)) File.Delete(outputAac);

                    var progress = new Progress<double>(value =>
                    {
                        StatusText = $"正在进行进程内转码... {value:P0}";
                    });
                    await _transcoder.TranscodeAsync(new MediaTranscodeRequest(
                        VideoFilePath, outputH264, outputG711a, outputAac,
                        originalFps, gop, 2_000_000, 4_000_000), progress, CancellationToken.None);
                    H264FilePath = outputH264;

                    if (hasAudio)
                    {
                        G711aFilePath = outputG711a!;
                        AacFilePath = outputAac!;
                    }

                    StatusText = $"转码完成就绪 ({TargetFps:F1} FPS{(hasAudio ? ", 带音频" : "")})";
                }

                // 提取视频第一帧作为占位画面
                StatusText = "正在生成占位图...";
                await ExtractThumbnailAsync(VideoFilePath);
                
                if (!string.IsNullOrEmpty(H264FilePath) && !Path.GetExtension(VideoFilePath).Equals(".h264", StringComparison.OrdinalIgnoreCase))
                {
                    bool hasAud = !string.IsNullOrEmpty(G711aFilePath);
                    StatusText = $"转码完成就绪 ({TargetFps:F1} FPS{(hasAud ? ", 带音频" : "")})";
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

        private IVideoPushSession? _pusher;

        public bool IsVideoViewVisible => IsStreaming || IsPreviewing;

        public bool IsPreviewBtnVisible => !string.IsNullOrEmpty(VideoFilePath) && !IsStreaming;

        public string PreviewButtonText => IsPreviewing ? "停止预览" : "预览";

        public string PlaceholderText
        {
            get
            {
                if (string.IsNullOrEmpty(VideoFilePath))
                {
                    return "未导入视频";
                }
                if (IsStreaming)
                {
                    return "正在推流";
                }
                if (IsPreviewing)
                {
                    return "正在预览";
                }
                return "就绪 - 可点击预览或等待推流";
            }
        }

        [RelayCommand]
        private void TogglePreview()
        {
            if (IsPreviewing)
            {
                StopPreviewing();
            }
            else
            {
                StartPreviewing();
            }
        }

        private Media CreateMedia(string filePath)
        {
            EnsureMediaPlayer();
            var media = new Media(_libVLC!, filePath, FromType.FromPath);
            media.AddOption(":input-repeat=65535"); // 无限循环
            if (Path.GetExtension(filePath).Equals(".h264", StringComparison.OrdinalIgnoreCase))
            {
                media.AddOption(":demux=h264"); // 针对原始 h264 裸流进行 demux 声明
            }
            return media;
        }

        public void StartPreviewing()
        {
            if (string.IsNullOrEmpty(VideoFilePath) || !File.Exists(VideoFilePath))
            {
                StatusText = "无有效视频源，无法预览";
                return;
            }

            if (IsStreaming)
            {
                StatusText = "正在推流中，无法预览";
                return;
            }

            IsPreviewing = true;
            IsMuted = true; // 默认静音

            Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(async () => 
            {
                var media = CreateMedia(VideoFilePath);
                var player = EnsureMediaPlayer();
                player.Play(media);
                await Task.Delay(200); // 延迟设置静音，等待VLC初始化音频输出
                player.Mute = IsMuted;
            }));
        }

        public void StopPreviewing()
        {
            if (!IsPreviewing) return;

            IsPreviewing = false;

            Application.Current?.Dispatcher?.Invoke(() => 
            {
                MediaPlayer?.Stop();
            });
        }

        public async Task StartPushingAsync(string ip, int port, string simCard, int dataType = 1, int audioCodec = 0)
        {
            if (dataType == 2 || dataType == 3)
            {
                // 纯音频模式
                if ((audioCodec == 0 && string.IsNullOrEmpty(G711aFilePath)) || 
                    (audioCodec == 1 && string.IsNullOrEmpty(AacFilePath)))
                {
                    StatusText = "无有效音频源，纯音频模式推流失败";
                    _logger?.Invoke("异常", $"通道 {LogicalChannelNo}: 平台请求音频流，但本地文件无音频轨道");
                    return;
                }
            }
            else
            {
                // 音视频或纯视频模式
                if (string.IsNullOrEmpty(H264FilePath) || !File.Exists(H264FilePath))
                {
                    StatusText = "无有效 H.264 视频源，推流失败";
                    return;
                }
            }

            if (dataType == 0 && string.IsNullOrEmpty(G711aFilePath))
            {
                _logger?.Invoke("系统", $"通道 {LogicalChannelNo}: 平台请求音视频流，但本地无音频轨道，将回退为仅推送纯视频。");
            }

            if (IsPreviewing)
            {
                StopPreviewing();
            }

            await StopPushingAsync();

            string? targetAudioFile = null;
            if (dataType == 0 || dataType == 2 || dataType == 3)
            {
                targetAudioFile = audioCodec == 0 ? G711aFilePath : AacFilePath;
                if (!File.Exists(targetAudioFile)) targetAudioFile = null;
            }

            _pusher = new Jt1078VideoPushSession(ip, port, simCard, LogicalChannelNo, H264FilePath, targetAudioFile ?? string.Empty, dataType, audioCodec, TargetFps, IsConstantFramerate);
            _pusher.Log += msg =>
            {
                _logger?.Invoke("音视频", $"通道 {LogicalChannelNo}: {msg}");
            };
            
            _lastTrafficUpdateTime = DateTime.MinValue;
            _lastTotalBytes = 0;
            TrafficText = "计算中...";

            _pusher.StatusUpdated += msg =>
            {
                Application.Current?.Dispatcher?.Invoke(() => 
                {
                    StatusText = msg;
                });
            };

            _pusher.Disconnected += () =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (IsStreaming)
                    {
                        IsStreaming = false;
                        StatusText = "推流连接已断开";
                        TrafficText = "";
                    }
                }));
            };
            _pusher.Error += ex => _logger?.Invoke("异常", $"通道 {LogicalChannelNo} 推流异常: {ex.Message}");

            try
            {
                await _pusher.StartAsync();
                IsStreaming = true;
            }
            catch (Exception ex)
            {
                await _pusher.DisposeAsync();
                _pusher = null;
                IsStreaming = false;
                StatusText = $"推流启动失败: {ex.Message}";
                TrafficText = "";
                _logger?.Invoke("异常", $"通道 {LogicalChannelNo} 推流启动失败: {ex.Message}");
                return;
            }
            IsMuted = true; // 默认静音

            // 播放视频并设置循环
            Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(async () => 
            {
                if (!string.IsNullOrEmpty(VideoFilePath) && File.Exists(VideoFilePath))
                {
                    var media = CreateMedia(VideoFilePath);
                    var player = EnsureMediaPlayer();
                    player.Play(media);
                    await Task.Delay(200); // 延迟设置静音，等待VLC初始化音频输出
                    player.Mute = IsMuted;
                }
            }));
        }

        public async Task StopPushingAsync()
        {
            var pusher = _pusher;
            _pusher = null;
            if (pusher != null)
            {
                await pusher.DisposeAsync();
            }
            IsStreaming = false;
            StatusText = "已停止推流";
            TrafficText = "";

            Application.Current?.Dispatcher?.Invoke(() => 
            {
                MediaPlayer?.Stop();
            });
        }

        public void StopPushing() => StopPushingAsync().GetAwaiter().GetResult();

        [RelayCommand]
        private void ToggleMute()
        {
            IsMuted = !IsMuted;
            if (MediaPlayer != null) MediaPlayer.Mute = IsMuted;
        }

        private async Task ExtractThumbnailAsync(string videoPath)
        {
            try
            {
                string thumbDir = Path.Combine(TerminalSimulation.Wpf.Helpers.PathHelper.ExeDir, "thumbnails");
                Directory.CreateDirectory(thumbDir);

                // Clean up old thumbnail if any
                if (!string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath))
                {
                    try { File.Delete(ThumbnailPath); }
                    catch (IOException ex) { ConsoleLogger.LogError("Thumbnail", "删除旧缩略图失败", ex); }
                }

                string thumbPath = Path.Combine(thumbDir, $"thumb_{LogicalChannelNo}_{Guid.NewGuid():N}.jpg");

                await _transcoder.WriteThumbnailAsync(videoPath, thumbPath, CancellationToken.None);

                if (File.Exists(thumbPath))
                {
                    ThumbnailPath = thumbPath;
                }
            }
            catch (Exception ex)
            {
                ConsoleLogger.LogError("Thumbnail", $"通道 {LogicalChannelNo} 提取缩略图失败: {ex.Message}");
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            await StopPushingAsync().ConfigureAwait(false);
            StopPreviewing();
            MediaPlayer?.Dispose();
            _libVLC?.Dispose();

            // 清理生成的缩略图临时文件
            if (!string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath))
            {
                try { File.Delete(ThumbnailPath); }
                catch (IOException ex) { ConsoleLogger.LogError("Thumbnail", "清理缩略图失败", ex); }
            }
            GC.SuppressFinalize(this);
        }
private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return $"{bytesPerSecond:F0} B/s";
            if (bytesPerSecond < 1024 * 1024)
                return $"{bytesPerSecond / 1024:F1} KB/s";
            return $"{bytesPerSecond / (1024 * 1024):F1} MB/s";
        }

        public void UpdateTraffic()
        {
            if (_pusher == null || !IsStreaming)
            {
                TrafficText = "";
                return;
            }

            long currentBytes = _pusher.TotalPushedBytes;
            var now = DateTime.Now;

            if (_lastTrafficUpdateTime == DateTime.MinValue)
            {
                _lastTrafficUpdateTime = now;
                _lastTotalBytes = currentBytes;
                TrafficText = "计算中...";
                return;
            }

            double seconds = (now - _lastTrafficUpdateTime).TotalSeconds;
            if (seconds > 0)
            {
                double speedBps = (currentBytes - _lastTotalBytes) / seconds;
                string speedText = FormatSpeed(speedBps);
                
                double totalMb = currentBytes / (1024.0 * 1024.0);
                string totalText = currentBytes < 1024 * 1024 ? $"{currentBytes / 1024.0:F1} KB" : $"{totalMb:F1} MB";

                TrafficText = $"{speedText} | 总计 {totalText}";
            }

            _lastTrafficUpdateTime = now;
            _lastTotalBytes = currentBytes;
        }

        public long CurrentPusherBytes => _pusher?.TotalPushedBytes ?? 0;

    }
}
