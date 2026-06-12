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
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class VideoChannelItem : ObservableObject, IDisposable
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
        [ObservableProperty] private string _h264FilePath = "";

        private LibVLC _libVLC;
        [ObservableProperty] private MediaPlayer _mediaPlayer;

        private readonly Action<string, string>? _logger;

        public VideoChannelItem(byte logicalChannelNo, Action<string, string>? logger = null)
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
                        var fileEx = new FileNotFoundException("未在本地找到 FFmpeg 转码组件，准备从备用网络源自动下载。");
                        _logger?.Invoke("异常", $"通道 {LogicalChannelNo}: {fileEx.Message}");
                        ConsoleLogger.LogError("FFmpeg", $"通道 {LogicalChannelNo}: 未在本地找到 FFmpeg，准备启动备用源下载。", fileEx);

                        StatusText = "正在下载 FFmpeg 转码组件...";
                        Directory.CreateDirectory(ffmpegPath);

                        var progress = new Progress<(double percent, double speed)> (t => 
                        {
                            Application.Current?.Dispatcher?.Invoke(() => 
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

                // 提取视频第一帧作为占位画面
                StatusText = "正在生成占位图...";
                await ExtractThumbnailAsync(VideoFilePath);
                StatusText = string.IsNullOrEmpty(H264FilePath) ? "处理失败" : (Path.GetExtension(VideoFilePath).Equals(".h264", StringComparison.OrdinalIgnoreCase) ? "H.264 原生流，已就绪" : $"转码完成就绪 ({TargetFps:F1} FPS)");
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
            var media = new Media(_libVLC, filePath, FromType.FromPath);
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

            Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => 
            {
                var media = CreateMedia(VideoFilePath);
                MediaPlayer.Play(media);
                MediaPlayer.Mute = true; // 强制静音
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

        public void StartPushing(string ip, int port, string simCard)
        {
            if (string.IsNullOrEmpty(H264FilePath))
            {
                StatusText = "无有效 H.264 视频源，无法推流";
                return;
            }

            if (IsPreviewing)
            {
                StopPreviewing();
            }

            StopPushing();

            _pusher = new TerminalSimulation.Protocol.JT1078Pusher(simCard, LogicalChannelNo, H264FilePath, TargetFps, IsConstantFramerate);
            _pusher.OnLog += msg => 
            {
                _logger?.Invoke("音视频", $"通道 {LogicalChannelNo}: {msg}");
            };
            _pusher.OnStatusUpdate += msg =>
            {
                Application.Current?.Dispatcher?.Invoke(() => 
                {
                    StatusText = msg;
                });
            };

            _ = _pusher.StartAsync(ip, port);
            IsStreaming = true;
            IsMuted = true; // 默认静音

            // 播放视频并设置循环
            Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => 
            {
                if (!string.IsNullOrEmpty(VideoFilePath) && File.Exists(VideoFilePath))
                {
                    var media = CreateMedia(VideoFilePath);
                    MediaPlayer.Play(media);
                    MediaPlayer.Mute = true; // 强制静音
                }
            }));
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

            Application.Current?.Dispatcher?.Invoke(() => 
            {
                MediaPlayer?.Stop();
            });
        }

        [RelayCommand]
        private void ToggleMute()
        {
            IsMuted = !IsMuted;
            MediaPlayer.Mute = IsMuted;
        }

        private void EnsureFFmpegPath()
        {
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            if (Directory.Exists(ffmpegPath) && File.Exists(Path.Combine(ffmpegPath, "ffmpeg.exe")))
            {
                FFmpeg.SetExecutablesPath(ffmpegPath);
            }
        }

        private async Task ExtractThumbnailAsync(string videoPath)
        {
            try
            {
                EnsureFFmpegPath();
                string thumbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thumbnails");
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
