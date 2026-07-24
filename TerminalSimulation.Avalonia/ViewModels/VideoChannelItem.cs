using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace TerminalSimulation.Avalonia.ViewModels
{
    public partial class VideoChannelItem : ObservableObject, IDisposable
    {
        /// <summary>
        /// Set by the View layer to enable file-picker dialogs from this ViewModel.
        /// </summary>
        internal static TopLevel? MainTopLevel { get; set; }

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

        [ObservableProperty] private bool _isConstantFramerate = true; // true = 均速推送, false = 原始帧率
        [ObservableProperty] private double _targetFps = 25.0;

        [ObservableProperty] private string _statusText = "空闲";
        [ObservableProperty] private string _trafficText = "";
        [ObservableProperty] private string _h264FilePath = "";
        [ObservableProperty] private string _g711aFilePath = "";
        [ObservableProperty] private string _aacFilePath = "";

        private long _lastTotalBytes = 0;
        private DateTime _lastTrafficUpdateTime = DateTime.MinValue;

        private LibVLC _libVLC;
        [ObservableProperty] private MediaPlayer _mediaPlayer;
        private Media? _currentMedia;

        private readonly Action<string, string>? _logger;

        public VideoChannelItem(byte logicalChannelNo, Action<string, string>? logger = null)
        {
            LogicalChannelNo = logicalChannelNo;
            _logger = logger;
            _libVLC = new LibVLC(enableDebugLogs: false);
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.EncounteredError += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = "VLC 播放失败";
                    _logger?.Invoke("播放器", $"通道 {LogicalChannelNo}: VLC 无法解码当前媒体");
                });
            };
        }

        [RelayCommand]
        private async Task SelectVideoFile()
        {
            var file = await TerminalSimulation.Avalonia.Helpers.DialogHelper.ShowOpenFileDialogAsync(
                "选择视频文件",
                new[]
                {
                    new global::Avalonia.Platform.Storage.FilePickerFileType("视频文件") { Patterns = new[] { "*.mp4", "*.h264", "*.avi", "*.mkv" } },
                    new global::Avalonia.Platform.Storage.FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
                });

            if (file != null)
            {
                VideoFilePath = file.Path.LocalPath;
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
                    string ffmpegPath = GetResolvedFFmpegPath();
                    if (!Directory.Exists(ffmpegPath) || !File.Exists(Path.Combine(ffmpegPath, "ffmpeg.exe")))
                    {
                        var fileEx = new FileNotFoundException("未在本地找到 FFmpeg 转码组件，准备从备用网络源自动下载。");
                        _logger?.Invoke("异常", $"通道 {LogicalChannelNo}: {fileEx.Message}");
                        ConsoleLogger.LogError("FFmpeg", $"通道 {LogicalChannelNo}: 未在本地找到 FFmpeg，准备启动备用源下载。", fileEx);

                        StatusText = "正在下载 FFmpeg 转码组件...";
                        Directory.CreateDirectory(ffmpegPath);

                        var progress = new Progress<(double percent, double speed)> (t =>
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                string speedText = FormatSpeed(t.speed);
                                StatusText = $"正在下载 FFmpeg 转码组件... {t.percent * 100:F1}% ({speedText})";
                            });
                        });

                        await DownloadFFmpegWithFallbackAsync(ffmpegPath, progress);
                        FFmpeg.SetExecutablesPath(ffmpegPath);
                    }
                    else
                    {
                        FFmpeg.SetExecutablesPath(ffmpegPath);
                    }

                    string h264Dir = Path.Combine(TerminalSimulation.Avalonia.Helpers.PathHelper.ExeDir, "h264");
                    Directory.CreateDirectory(h264Dir);
                    string outputH264 = Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.h264");

                    if (File.Exists(outputH264))
                    {
                        File.Delete(outputH264);
                    }

                    var mediaInfo = await FFmpeg.GetMediaInfo(VideoFilePath);
                    bool hasAudio = mediaInfo.AudioStreams.Any();
                    var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

                    // 提取原始视频帧率，如果提取失败则默认 25
                    double originalFps = videoStream?.Framerate ?? 25.0;
                    if (originalFps <= 0) originalFps = 25.0;

                    TargetFps = originalFps;
                    int gop = (int)Math.Max(10, Math.Round(originalFps * 2)); // 2秒一个关键帧

                    // 重新编码为标准流：保持原帧率，2秒一个关键帧，使用 CRF 26 并限制最高码率防止网络崩溃
                    var conversion = FFmpeg.Conversions.New()
                        .AddParameter($"-i \"{VideoFilePath}\"")
                        .AddParameter($"-c:v libx264 -preset veryfast -r {originalFps} -g {gop} -crf 26 -maxrate 2M -bufsize 4M -bf 0 -an -f h264")
                        .SetOutput(outputH264);

                    conversion.OnProgress += (sender, args) =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText = $"正在转码视频... {args.Percent}%";
                        });
                    };

                    await conversion.Start();
                    H264FilePath = outputH264;

                    if (hasAudio)
                    {
                        string outputG711a = Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.g711a");
                        if (File.Exists(outputG711a)) File.Delete(outputG711a);

                        string outputAac = Path.Combine(h264Dir, $"{Path.GetFileNameWithoutExtension(VideoFilePath)}_{LogicalChannelNo}.aac");
                        if (File.Exists(outputAac)) File.Delete(outputAac);

                        var audioConversion = FFmpeg.Conversions.New()
                            .AddParameter($"-i \"{VideoFilePath}\"")
                            .AddParameter("-vn -c:a pcm_alaw -ar 8000 -ac 1 -f alaw")
                            .SetOutput(outputG711a);

                        var aacConversion = FFmpeg.Conversions.New()
                            .AddParameter($"-i \"{VideoFilePath}\"")
                            .AddParameter("-vn -c:a aac -b:a 64k -ar 8000 -ac 1 -f adts")
                            .SetOutput(outputAac);

                        audioConversion.OnProgress += (sender, args) =>
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                StatusText = $"正在提取音频... {args.Percent}%";
                            });
                        };

                        await audioConversion.Start();
                        await aacConversion.Start();

                        G711aFilePath = outputG711a;
                        AacFilePath = outputAac;
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

        private TerminalSimulation.Protocol.JT1078Pusher? _pusher;

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
            _currentMedia?.Dispose();
            var media = new Media(_libVLC, filePath, FromType.FromPath);
            _currentMedia = media;
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

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var media = CreateMedia(VideoFilePath);
                await Task.Delay(500); // Give Avalonia time to measure the VideoView before LibVLC attaches
                if (!MediaPlayer.Play(media))
                {
                    media.Dispose();
                    _currentMedia = null;
                    IsPreviewing = false;
                    StatusText = "VLC 播放失败";
                    return;
                }
                await Task.Delay(200); // 延迟设置静音，等待VLC初始化音频输出
                MediaPlayer.Mute = IsMuted;
            }, DispatcherPriority.Loaded);
        }

        public void StopPreviewing()
        {
            if (!IsPreviewing) return;

            IsPreviewing = false;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                MediaPlayer?.Stop();
            });
        }

        public void StartPushing(string ip, int port, string simCard, int dataType = 1, int audioCodec = 0)
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

            StopPushing();

            string? targetAudioFile = null;
            if (dataType == 0 || dataType == 2 || dataType == 3)
            {
                targetAudioFile = audioCodec == 0 ? G711aFilePath : AacFilePath;
                if (!File.Exists(targetAudioFile)) targetAudioFile = null;
            }

            _pusher = new TerminalSimulation.Protocol.JT1078Pusher(simCard, LogicalChannelNo, H264FilePath, targetAudioFile, dataType, audioCodec, TargetFps, IsConstantFramerate);
            _pusher.OnLog += msg =>
            {
                _logger?.Invoke("音视频", $"通道 {LogicalChannelNo}: {msg}");
            };

            _lastTrafficUpdateTime = DateTime.MinValue;
            _lastTotalBytes = 0;
            TrafficText = "计算中...";

            _pusher.OnStatusUpdate += msg =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText = msg;
                });
            };

            _pusher.OnDisconnected += () =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsStreaming)
                    {
                        StopPushing();
                    }
                });
            };

            _ = _pusher.StartAsync(ip, port);
            IsStreaming = true;
            IsMuted = true; // 默认静音

            // 播放视频并设置循环
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!string.IsNullOrEmpty(VideoFilePath) && File.Exists(VideoFilePath))
                {
                    var media = CreateMedia(VideoFilePath);
                    await Task.Delay(500); // Give Avalonia time to measure the VideoView before LibVLC attaches
                    if (!MediaPlayer.Play(media))
                    {
                        media.Dispose();
                        _currentMedia = null;
                        StatusText = "VLC 播放失败";
                        return;
                    }
                    await Task.Delay(200); // 延迟设置静音，等待VLC初始化音频输出
                    MediaPlayer.Mute = IsMuted;
                }
            }, DispatcherPriority.Loaded);
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
            TrafficText = "";

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                MediaPlayer?.Stop();
                _currentMedia?.Dispose();
                _currentMedia = null;
            });
        }
        public void SuspendPlayback()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                MediaPlayer?.Stop();
                _currentMedia?.Dispose();
                _currentMedia = null;
            });
        }

        public void ResumePlayback()
        {
            if (!IsPreviewing && !IsStreaming) return;

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!string.IsNullOrEmpty(VideoFilePath) && File.Exists(VideoFilePath))
                {
                    var media = CreateMedia(VideoFilePath);
                    await Task.Delay(500); // Give Avalonia plenty of time to construct and arrange the HWND after tab switch
                    if (MediaPlayer != null && !MediaPlayer.Play(media))
                    {
                        media.Dispose();
                        _currentMedia = null;
                        StatusText = "VLC 播放失败";
                        return;
                    }
                    await Task.Delay(200);
                    if (MediaPlayer != null) MediaPlayer.Mute = IsMuted;
                }
            }, DispatcherPriority.Loaded);
        }

        [RelayCommand]
        private void ToggleMute()
        {
            IsMuted = !IsMuted;
            MediaPlayer.Mute = IsMuted;
        }

        private void EnsureFFmpegPath()
        {
            string ffmpegPath = GetResolvedFFmpegPath();
            if (Directory.Exists(ffmpegPath) && File.Exists(Path.Combine(ffmpegPath, "ffmpeg.exe")))
            {
                FFmpeg.SetExecutablesPath(ffmpegPath);
            }
        }

        private string GetResolvedFFmpegPath()
        {
            // First check if it's bundled in AppDir (extracted temp folder)
            string appFFmpeg = Path.Combine(TerminalSimulation.Avalonia.Helpers.PathHelper.AppDir, "ffmpeg");
            if (Directory.Exists(appFFmpeg) && File.Exists(Path.Combine(appFFmpeg, "ffmpeg.exe")))
            {
                return appFFmpeg;
            }

            // Otherwise, default to ExeDir so downloads persist
            return Path.Combine(TerminalSimulation.Avalonia.Helpers.PathHelper.ExeDir, "ffmpeg");
        }

        private async Task ExtractThumbnailAsync(string videoPath)
        {
            try
            {
                EnsureFFmpegPath();
                string thumbDir = Path.Combine(TerminalSimulation.Avalonia.Helpers.PathHelper.ExeDir, "thumbnails");
                Directory.CreateDirectory(thumbDir);

                // Clean up old thumbnail if any
                if (!string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath))
                {
                    try { File.Delete(ThumbnailPath); } catch { }
                }

                string thumbPath = Path.Combine(thumbDir, $"thumb_{LogicalChannelNo}_{Guid.NewGuid():N}.jpg");

                // Extract the first frame as JPEG
                var conversion = FFmpeg.Conversions.New()
                    .AddParameter($"-ss 00:00:00 -i \"{videoPath}\" -vframes 1 -f image2 -q:v 2 \"{thumbPath}\"");

                await conversion.Start();

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

        public void Dispose()
        {
            StopPushing();
            StopPreviewing();
            _currentMedia?.Dispose();
            _currentMedia = null;
            MediaPlayer?.Dispose();
            _libVLC?.Dispose();

            // 清理生成的缩略图临时文件
            if (!string.IsNullOrEmpty(ThumbnailPath) && File.Exists(ThumbnailPath))
            {
                try { File.Delete(ThumbnailPath); } catch { }
            }
        }

        private async Task DownloadFFmpegWithFallbackAsync(string destinationFolder, IProgress<(double percent, double speed)> progress)
        {
            string[] sources = new string[]
            {
                // 推荐源 1: ghproxy.net (国内高速 CDN 加速代理，无限制)
                "https://ghproxy.net/https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",

                // 推荐源 2: gh-proxy.org (国内高速 CDN 加速代理)
                "https://gh-proxy.org/https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",

                // Gyan.dev 官方推荐 Windows 静态包源
                "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",

                // GitHub 官方原生源
                "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
            };

            _logger?.Invoke("系统", "正在对所有下载源进行延迟测速，以选择最优下载地址...");
            ConsoleLogger.LogInfo("正在对所有下载源进行延迟测速，以选择最优下载地址...");

            string[] sortedSources;
            try
            {
                sortedSources = await SortSourcesBySpeedAsync(sources);
            }
            catch (Exception ex)
            {
                ConsoleLogger.LogError("FFmpegDownloader", "测速时发生异常，使用默认源顺序", ex);
                sortedSources = sources;
            }

            Exception? lastException = null;

            foreach (var url in sortedSources)
            {
                try
                {
                    _logger?.Invoke("系统", $"正在尝试从以下源下载 FFmpeg: {url}");
                    ConsoleLogger.LogInfo($"开始尝试从 {url} 下载 FFmpeg...");

                    await DownloadAndExtractZipAsync(url, destinationFolder, progress);

                    if (File.Exists(Path.Combine(destinationFolder, "ffmpeg.exe")))
                    {
                        _logger?.Invoke("系统", "FFmpeg 下载并解压完成。");
                        ConsoleLogger.LogInfo("FFmpeg 下载并解压成功！");

                        // Try caching downloaded FFmpeg back to the project's ./FFmpeg folder for future builds
                        try
                        {
                            string? projectFFmpegPath = FindProjectFFmpegPath();
                            if (!string.IsNullOrEmpty(projectFFmpegPath))
                            {
                                Directory.CreateDirectory(projectFFmpegPath);
                                CopyFileIfExists(Path.Combine(destinationFolder, "ffmpeg.exe"), Path.Combine(projectFFmpegPath, "ffmpeg.exe"));
                                CopyFileIfExists(Path.Combine(destinationFolder, "ffprobe.exe"), Path.Combine(projectFFmpegPath, "ffprobe.exe"));
                                ConsoleLogger.LogInfo($"已将下载的 FFmpeg 缓存复制到工程目录: {projectFFmpegPath}");
                            }
                        }
                        catch (Exception cacheEx)
                        {
                            ConsoleLogger.LogError("FFmpegDownloader", "尝试缓存到工程目录失败", cacheEx);
                        }

                        return;
                    }

                    throw new FileNotFoundException("下载完成，但未在解压目录中找到 ffmpeg.exe");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger?.Invoke("异常", $"从该源下载 FFmpeg 失败: {ex.Message}");
                    ConsoleLogger.LogError("FFmpegDownloader", $"从 {url} 下载失败", ex);
                }
            }

            throw new Exception("所有 FFmpeg 下载源尝试均失败！", lastException);
        }

        private async Task DownloadAndExtractZipAsync(string url, string destinationFolder, IProgress<(double percent, double speed)> progress)
        {
            using var client = new System.Net.Http.HttpClient();
            using var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var contentStream = await response.Content.ReadAsStreamAsync();

            string tempZipFile = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");

            try
            {
                var stopwatch = Stopwatch.StartNew();
                using (var fileStream = new FileStream(tempZipFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    byte[] buffer = new byte[8192];
                    long totalReadBytes = 0;
                    int readBytes;

                    while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, readBytes);
                        totalReadBytes += readBytes;

                        double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                        double currentSpeed = elapsedSeconds > 0.1 ? totalReadBytes / elapsedSeconds : 0;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double percent = (double)totalReadBytes / totalBytes.Value;
                            progress.Report((percent, currentSpeed));
                        }
                        else
                        {
                            progress.Report((0.0, currentSpeed));
                        }
                    }
                }

                _logger?.Invoke("系统", "正在解压 FFmpeg 压缩包...");
                ConsoleLogger.LogInfo("正在解压 FFmpeg 压缩包...");

                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                using (var archive = System.IO.Compression.ZipFile.OpenRead(tempZipFile))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string name = entry.Name.ToLower();
                        if (name == "ffmpeg.exe" || name == "ffprobe.exe")
                        {
                            string destinationPath = Path.Combine(destinationFolder, entry.Name);
                            if (File.Exists(destinationPath))
                            {
                                File.Delete(destinationPath);
                            }
                            entry.ExtractToFile(destinationPath);
                            ConsoleLogger.LogInfo($"已成功解压并提取: {entry.Name}");
                        }
                    }
                }
            }
            finally
            {
                if (File.Exists(tempZipFile))
                {
                    try { File.Delete(tempZipFile); } catch { }
                }
            }
        }

        private async Task<string[]> SortSourcesBySpeedAsync(string[] sources)
        {
            var tasks = sources.Select(async url =>
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(3);

                    var sw = Stopwatch.StartNew();
                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                    using var response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    sw.Stop();

                    if (response.IsSuccessStatusCode)
                    {
                        return (url, latency: sw.ElapsedMilliseconds);
                    }
                }
                catch
                {
                    try
                    {
                        using var client = new System.Net.Http.HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(3);
                        var sw = Stopwatch.StartNew();
                        using var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        sw.Stop();

                        if (response.IsSuccessStatusCode)
                        {
                            return (url, latency: sw.ElapsedMilliseconds);
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }
                return (url, latency: long.MaxValue);
            });

            var results = await Task.WhenAll(tasks);

            foreach (var r in results.OrderBy(x => x.latency))
            {
                string latencyText = r.latency == long.MaxValue ? "测试失败/超时" : $"{r.latency}ms";
                ConsoleLogger.LogInfo($"源: {r.url} | 响应延迟: {latencyText}");
            }

            return results
                .OrderBy(r => r.latency)
                .Select(r => r.url)
                .ToArray();
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

        private string? FindProjectFFmpegPath()
        {
            string? current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(current)) break;

                if (File.Exists(Path.Combine(current, "TerminalSimulation.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
                {
                    return Path.Combine(current, "FFmpeg");
                }
                current = Path.GetDirectoryName(current);
            }
            return null;
        }

        private void CopyFileIfExists(string source, string dest)
        {
            if (File.Exists(source))
            {
                File.Copy(source, dest, true);
            }
        }
    }
}
