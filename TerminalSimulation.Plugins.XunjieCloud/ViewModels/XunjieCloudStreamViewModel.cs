using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using TerminalSimulation.Plugins.XunjieCloud.Services;
using System.Linq;
using System.Text.RegularExpressions;

namespace TerminalSimulation.Plugins.XunjieCloud.ViewModels
{
    public class NetworkLogItem
    {
        public string Time { get; set; } = "";
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class StreamDataType
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    public partial class XunjieCloudStreamViewModel : ObservableObject, IDisposable
    {
        // Login properties
        [ObservableProperty] private bool _isLoggedIn = false;
        [ObservableProperty] private string _username = "";
        [ObservableProperty] private string _password = "";
        
        private string _selectedUsername = "";
        public string SelectedUsername
        {
            get => _selectedUsername;
            set
            {
                if (SetProperty(ref _selectedUsername, value))
                {
                    Username = value;
                    var settings = UtilitySettingsManager.LoadSettings();
                    var acc = settings.SavedAccounts.Find(a => a.Username == value);
                    if (acc != null)
                    {
                        Password = UtilitySettingsManager.Decrypt(acc.EncryptedPassword);
                        if (!string.IsNullOrWhiteSpace(acc.LastDeviceNo))
                        {
                            DeviceNo = acc.LastDeviceNo;
                        }
                    }
                }
            }
        }
        
        public ObservableCollection<string> SavedUsernames { get; } = new();

        // Device control properties
        [ObservableProperty] private string _deviceNo = "13802603352";
        [ObservableProperty] private string _token = "";
        [ObservableProperty] private string _statusText = "空闲";
        [ObservableProperty] private bool _isPlaying = false;
        [ObservableProperty] private bool _isVideoViewVisible = false;
        
        [ObservableProperty] private ObservableCollection<string> _availableChannels = new();
        [ObservableProperty] private string? _selectedChannel;

        // Advanced features
        [ObservableProperty] private string _currentBitrate = "0 KB/s";
        [ObservableProperty] private string _hasAudioTrack = "检测中...";
        private int _volume = 100;
        public int Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value))
                {
                    if (MediaPlayer != null) MediaPlayer.Volume = value;
                }
            }
        }

        public ObservableCollection<StreamDataType> DataTypes { get; } = new();
        [ObservableProperty] private StreamDataType? _selectedDataType;

        public ObservableCollection<NetworkLogItem> NetworkLogs { get; } = new();

        private LibVLC? _libVLC;
        [ObservableProperty] private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;

        private int _retryCount = 0;
        private const int MaxRetries = 3;
        private CancellationTokenSource? _retryCancellation;
        private DispatcherTimer? _statsTimer;
        
        private long _lastReadBytes = 0;
        private DateTime _lastReadTime = DateTime.MinValue;
        private int _audioDetectTicks = 0;

        public XunjieCloudStreamViewModel()
        {
            DataTypes.Add(new StreamDataType { Name = "音视频 (Audio & Video)", Value = 0 });
            DataTypes.Add(new StreamDataType { Name = "仅视频 (Video Only)", Value = 1 });
            DataTypes.Add(new StreamDataType { Name = "双向对讲 (Two-way Audio)", Value = 2 });
            DataTypes.Add(new StreamDataType { Name = "仅监听 (Listen Only)", Value = 3 });
            SelectedDataType = DataTypes[1]; // Default to Video Only
            
            LoadSavedAccounts();

            try
            {
                // 开启调试日志以抓取底层网络请求
                _libVLC = new LibVLC(enableDebugLogs: true);
                _libVLC.Log += LibVLC_Log;

                MediaPlayer = new MediaPlayer(_libVLC);
                MediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                MediaPlayer.Playing += MediaPlayer_Playing;
                MediaPlayer.Buffering += MediaPlayer_Buffering;
                MediaPlayer.EndReached += MediaPlayer_EndReached;

                // 初始化网速监控定时器
                _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _statsTimer.Tick += StatsTimer_Tick;
            }
            catch (Exception ex)
            {
                LogNetwork("ERROR", $"VLC 初始化失败: {ex.Message}");
            }
        }

        private void LoadSavedAccounts()
        {
            SavedUsernames.Clear();
            var settings = UtilitySettingsManager.LoadSettings();
            foreach (var acc in settings.SavedAccounts)
            {
                SavedUsernames.Add(acc.Username);
            }
            if (SavedUsernames.Count > 0)
            {
                SelectedUsername = SavedUsernames[0];
            }
        }

        private void LogNetwork(string type, string message)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss.fff");
                NetworkLogs.Insert(0, new NetworkLogItem { Time = time, Type = type, Message = message });
                if (NetworkLogs.Count > 200) NetworkLogs.RemoveAt(NetworkLogs.Count - 1);
            });
        }

        private void LibVLC_Log(object? sender, LogEventArgs e)
        {
            // 过滤底层拉流的日志，模拟网络抓包
            string msg = e.Message ?? "";
            if (msg.Contains("http access") || msg.Contains("https") || msg.Contains(".ts") || msg.Contains(".m3u8"))
            {
                if (msg.Contains("access out: ")) return; // 忽略不必要的日志
                
                string type = "Network";
                if (msg.Contains(".m3u8")) type = "Manifest";
                else if (msg.Contains(".ts")) type = "Segment";

                // 提取 URL
                var match = Regex.Match(msg, @"(https?://[^\s]+)");
                if (match.Success)
                {
                    LogNetwork(type, $"GET {match.Value}");
                }
                else if (msg.Contains("downloading"))
                {
                    LogNetwork(type, msg);
                }
            }
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            if (MediaPlayer != null && MediaPlayer.IsPlaying)
            {
                var stats = MediaPlayer.Media?.Statistics;
                if (stats.HasValue)
                {
                    long currentBytes = stats.Value.DemuxReadBytes;
                    var now = DateTime.Now;

                    if (_lastReadTime != DateTime.MinValue && currentBytes >= _lastReadBytes)
                    {
                        double seconds = (now - _lastReadTime).TotalSeconds;
                        if (seconds > 0)
                        {
                            double bytesPerSec = (currentBytes - _lastReadBytes) / seconds;
                            if (bytesPerSec > 1024 * 1024)
                            {
                                CurrentBitrate = $"{(bytesPerSec / (1024 * 1024)):F2} MB/s";
                            }
                            else
                            {
                                CurrentBitrate = $"{(bytesPerSec / 1024):F1} KB/s";
                            }
                        }
                    }
                    else if (currentBytes < _lastReadBytes)
                    {
                        // Handle counter reset or new stream
                        _lastReadBytes = currentBytes;
                    }

                    _lastReadBytes = currentBytes;
                    _lastReadTime = now;
                }

                // 持续检测音轨，因为 HLS 的音轨经常是滞后解析出来的
                if (MediaPlayer.Media != null && (HasAudioTrack == "检测中..." || HasAudioTrack == "无音频流"))
                {
                    bool hasAudio = MediaPlayer.Media.Tracks.Any(t => t.TrackType == TrackType.Audio);
                    if (hasAudio)
                    {
                        HasAudioTrack = "包含音频";
                    }
                    else
                    {
                        _audioDetectTicks++;
                        if (_audioDetectTicks >= 5)
                        {
                            HasAudioTrack = "未检测到音频";
                        }
                    }
                }
            }
            else
            {
                CurrentBitrate = "0.0 KB/s";
                _lastReadTime = DateTime.MinValue;
            }
        }

        [RelayCommand]
        private async Task LoginAsync(object? parameter)
        {
            if (parameter is System.Windows.Controls.PasswordBox pb)
            {
                Password = pb.Password;
            }

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                LogNetwork("System", "账号和密码不能为空");
                return;
            }

            LogNetwork("System", "正在登录...");
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("platform-id", "0");
                var payload = new { username = Username.Trim(), password = Password, loginType = "0" };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                LogNetwork("Auth", "POST https://wb.xajyun.com/prod-api/auth/login");
                var response = await client.PostAsync("https://wb.xajyun.com/prod-api/auth/login", content);
                var responseStr = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseStr);
                if (doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() == 200)
                {
                    var token = doc.RootElement.GetProperty("data").GetProperty("access_token").GetString();
                    if (!string.IsNullOrEmpty(token))
                    {
                        Token = token;
                        IsLoggedIn = true;
                        LogNetwork("Auth", "登录成功");
                        
                        UtilitySettingsManager.SaveAccount(Username.Trim(), Password);
                        if (!SavedUsernames.Contains(Username.Trim()))
                        {
                            Application.Current?.Dispatcher?.InvokeAsync(() => SavedUsernames.Add(Username.Trim()));
                        }
                    }
                }
                else
                {
                    var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : "未知错误";
                    LogNetwork("Auth", $"登录失败: {msg}");
                }
            }
            catch (Exception ex)
            {
                LogNetwork("Auth", $"登录异常: {ex.Message}");
            }
        }

        [RelayCommand]
        private void Logout()
        {
            IsLoggedIn = false;
            Token = string.Empty;
            LogNetwork("System", "已退出登录");
        }

        [RelayCommand]
        private async Task FetchChannelsAsync()
        {
            if (string.IsNullOrWhiteSpace(DeviceNo))
            {
                LogNetwork("System", "设备号不能为空");
                return;
            }

            LogNetwork("System", $"正在获取设备 {DeviceNo} 的通道列表...");
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("platform-id", "0");
                if (!string.IsNullOrWhiteSpace(Token))
                {
                    client.DefaultRequestHeaders.Add("Authorization", Token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? Token : "Bearer " + Token);
                }

                var payload = new { page = 1, pageSize = 10, params_ = new { input = DeviceNo.Trim() } };
                var json = JsonSerializer.Serialize(payload).Replace("params_", "params");
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                LogNetwork("API", "POST .../getList");
                var respList = await client.PostAsync("https://wb.xajyun.com/prod-api/map/rearviewMirror/getList", content);
                var respListStr = await respList.Content.ReadAsStringAsync();
                
                using var docList = JsonDocument.Parse(respListStr);
                var bindObjectId = "";
                if (docList.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("list", out var list) && list.GetArrayLength() > 0)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.TryGetProperty("rearviewMirrorCode", out var code) && code.GetString() == DeviceNo.Trim())
                        {
                            bindObjectId = item.GetProperty("bindObjectId").GetString();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bindObjectId))
                {
                    LogNetwork("API", "未找到该设备的绑定信息");
                    return;
                }

                LogNetwork("API", $"GET .../getChannelsNum?bindObjectId={bindObjectId}");
                var respChannels = await client.GetAsync($"https://wb.xajyun.com/prod-api/map/rearviewMirror/getChannelsNum?bindObjectId={bindObjectId}");
                var respChannelsStr = await respChannels.Content.ReadAsStringAsync();
                
                using var docChannels = JsonDocument.Parse(respChannelsStr);
                if (docChannels.RootElement.TryGetProperty("code", out var statusCode) && statusCode.GetInt32() == 200)
                {
                    int channelsNum = docChannels.RootElement.GetProperty("data").GetInt32();
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        AvailableChannels.Clear();
                        for (int i = 1; i <= channelsNum; i++)
                        {
                            AvailableChannels.Add(i.ToString());
                        }
                        if (AvailableChannels.Count > 0) SelectedChannel = AvailableChannels[0];
                        LogNetwork("System", $"成功获取 {channelsNum} 个通道");
                        if (IsLoggedIn)
                        {
                            UtilitySettingsManager.UpdateLastDeviceNo(Username, DeviceNo.Trim());
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogNetwork("System", $"获取通道异常: {ex.Message}");
            }
        }

        private async Task SendDownlinkCommandAsync(int msgId, object msgBody)
        {
            if (string.IsNullOrWhiteSpace(SelectedChannel))
            {
                LogNetwork("System", "请先选择一个通道");
                return;
            }

            LogNetwork("System", $"正在下发指令 {msgId} ...");
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("platform-id", "0");
                if (!string.IsNullOrWhiteSpace(Token))
                {
                    client.DefaultRequestHeaders.Add("Authorization", Token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? Token : "Bearer " + Token);
                }

                var payload = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "clientId", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                    { "imei", DeviceNo.Trim() },
                    { "msgId", msgId }
                };
                
                if (msgId == 37121) payload.Add("msg9101", msgBody);
                else if (msgId == 37122) payload.Add("msg9102", msgBody);

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                LogNetwork("Command", $"POST .../downlinkCommand [{msgId}]");
                var response = await client.PostAsync("https://wb.xajyun.com/prod-api/map/iot/downlinkCommand", content);
                var responseStr = await response.Content.ReadAsStringAsync();

                LogNetwork("Command", $"指令响应: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                LogNetwork("System", $"下发指令异常: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task OpenChannelAsync()
        {
            int dataType = SelectedDataType?.Value ?? 1;
            var msg9101 = new { logicChannelId = SelectedChannel?.Trim(), dataType = dataType, streamType = 1 };
            await SendDownlinkCommandAsync(37121, msg9101);
        }

        [RelayCommand]
        private async Task CloseChannelAsync()
        {
            var msg9102 = new { logicChannelId = SelectedChannel?.Trim(), commandType = 0, closeAvType = 0, switchStreamType = 0 };
            await SendDownlinkCommandAsync(37122, msg9102);
        }

        [RelayCommand]
        private void StartPlay()
        {
            if (string.IsNullOrWhiteSpace(DeviceNo) || string.IsNullOrWhiteSpace(SelectedChannel))
            {
                LogNetwork("System", "设备号或通道号不能为空");
                return;
            }
            if (_libVLC == null || MediaPlayer == null) return;

            // 如果是人为主动点击拉流，则重置重试次数
            _retryCancellation?.Cancel();
            _retryCancellation?.Dispose();
            _retryCancellation = new CancellationTokenSource();
            _retryCount = 0;
            ExecuteStartPlay();
        }

        private void ExecuteStartPlay()
        {
            if (_libVLC == null || MediaPlayer == null) return;

            if (MediaPlayer.IsPlaying) MediaPlayer.Stop();
            _currentMedia?.Dispose();
            _currentMedia = null;

            string url = $"https://live.xajyun.com/hls/{DeviceNo.Trim()}_{SelectedChannel?.Trim()}/playlist.m3u8";
            LogNetwork("Player", $"尝试拉取视频流: {url}");
            StatusText = "正在连接...";
            HasAudioTrack = "检测中...";
            _audioDetectTicks = 0;
            CurrentBitrate = "0 KB/s";
            _lastReadTime = DateTime.MinValue;
            _lastReadBytes = 0;

            var media = new Media(_libVLC, url, FromType.FromLocation);
            _currentMedia = media;
            
            // 底层缓存与防卡顿优化
            media.AddOption(":avcodec-hw=any"); // 开启硬解
            // 去除死板的静态缓存，使用针对 HLS 直播更友好的实时边沿缓冲策略
            media.AddOption(":hls-live-edge=3"); // 从直播边沿保留3个分片
            media.AddOption(":network-caching=1500"); // 基础网络抗抖动
            media.AddOption(":live-caching=1500"); // 针对直播流的抗抖动缓存

            if (!MediaPlayer.Play(media))
            {
                media.Dispose();
                _currentMedia = null;
                IsPlaying = false;
                IsVideoViewVisible = false;
                StatusText = "播放失败";
                LogNetwork("Player", "VLC 未能开始播放该媒体");
                return;
            }
            MediaPlayer.Volume = Volume;
            IsPlaying = true;
            IsVideoViewVisible = false;
            _statsTimer?.Start();

            if (IsLoggedIn)
            {
                UtilitySettingsManager.UpdateLastDeviceNo(Username, DeviceNo.Trim());
            }
        }

        [RelayCommand]
        private void StopPlay()
        {
            _retryCancellation?.Cancel();
            _retryCount = MaxRetries; // 主动停止，阻止重连机制
            _retryCancellation?.Cancel();
            if (MediaPlayer != null && MediaPlayer.IsPlaying)
            {
                MediaPlayer.Stop();
                LogNetwork("Player", "主动停止拉流");
            }
            _currentMedia?.Dispose();
            _currentMedia = null;
            IsPlaying = false;
            IsVideoViewVisible = false;
            StatusText = "已停止";
            CurrentBitrate = "0.0 KB/s";
            _lastReadTime = DateTime.MinValue;
            _lastReadBytes = 0;
            HasAudioTrack = "无";
            _statsTimer?.Stop();
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            LogNetwork("Player", "播放结束");
            Application.Current?.Dispatcher?.InvokeAsync(() => { IsPlaying = false; IsVideoViewVisible = false; _statsTimer?.Stop(); });
        }

        private void MediaPlayer_Buffering(object? sender, MediaPlayerBufferingEventArgs e)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() => StatusText = $"缓冲中... {e.Cache}%");
        }

        private async void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            LogNetwork("Player", "开始播放");
            _retryCount = 0; // 播放成功，重置重连计数
            Application.Current?.Dispatcher?.Invoke(() => 
            { 
                StatusText = "正在播放"; 
                IsPlaying = true;
            });
            
            // 延迟 300 毫秒，确保底层 D3D 画布已输出第一帧，防止白屏闪烁
            await Task.Delay(300);
            Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (IsPlaying)
                {
                    IsVideoViewVisible = true;
                }
            }));
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            var cancellationToken = _retryCancellation?.Token ?? CancellationToken.None;
            LogNetwork("Error", "拉取流发生错误或流已断开");
            Application.Current?.Dispatcher?.InvokeAsync(async () => 
            { 
                StatusText = "播放错误"; 
                IsPlaying = false;
                IsVideoViewVisible = false;
                _statsTimer?.Stop();
                CurrentBitrate = "0 KB/s";

                // 重试机制
                if (_retryCount < MaxRetries)
                {
                    _retryCount++;
                    LogNetwork("Retry", $"等待 2 秒后进行第 {_retryCount} 次重试...");
                    try
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    if (cancellationToken.IsCancellationRequested) return;
                    ExecuteStartPlay();
                }
                else
                {
                    LogNetwork("Retry", "重试次数已达上限，停止重连。");
                }
            });
        }

        public void Dispose()
        {
            _retryCancellation?.Cancel();
            _retryCancellation?.Dispose();
            _retryCancellation = null;
            StopPlay();
            _statsTimer?.Stop();
            
            if (MediaPlayer != null)
            {
                MediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                MediaPlayer.Playing -= MediaPlayer_Playing;
                MediaPlayer.Buffering -= MediaPlayer_Buffering;
                MediaPlayer.EndReached -= MediaPlayer_EndReached;
                MediaPlayer.Dispose();
                MediaPlayer = null;
            }
            _currentMedia?.Dispose();
            _currentMedia = null;
            if (_libVLC != null)
            {
                _libVLC.Log -= LibVLC_Log;
                _libVLC.Dispose();
                _libVLC = null;
            }
        }
    }
}
