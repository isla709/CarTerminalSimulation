using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.Extensions;
using JT808.Protocol.MessageBody;
using TerminalSimulation.Network;
using TerminalSimulation.Protocol;
using System.Speech.Synthesis;

using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using TerminalSimulation.Wpf.Services;

namespace TerminalSimulation.Wpf.ViewModels
{
    public enum BackgroundEffectMode
    {
        None,
        Translucent,
        Blur,
        Acrylic
    }

    public class RegionNode
    {
        public string code { get; set; } = "";
        public string name { get; set; } = "";
        public System.Collections.Generic.List<RegionNode> children { get; set; } = new();
    }

    public class PlateColorItem
    {
        public byte Value { get; set; }
        public string Name { get; set; } = "";
    }

    public class AnalyzerNode
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public ObservableCollection<AnalyzerNode> Children { get; } = new();
    }

    public class AnalyzerTableRow
    {
        public int Index { get; set; }
        public string Field { get; set; } = "";
        public string HexData { get; set; } = "";
        public string DataType { get; set; } = "";
        public string OffsetStr { get; set; } = "";
        public string LengthStr { get; set; } = "";
        public string Result { get; set; } = "";
    }

    public partial class LogMessageItem : ObservableObject
    {
        [ObservableProperty] private string _timestampStr = "";
        [ObservableProperty] private string _directionStr = "";
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private bool _hasRaw = false;
        [ObservableProperty] private string _rawData = "";

        [RelayCommand]
        private void CopyRaw()
        {
            if (!string.IsNullOrEmpty(RawData))
            {
                System.Windows.Clipboard.SetText(RawData);
            }
        }

        [RelayCommand]
        private void CopyFullLog()
        {
            string fullText = $"{TimestampStr} {DirectionStr} {Message}".Trim();
            System.Windows.Clipboard.SetText(fullText);
        }
    }

    public partial class ThemeImageItem : ObservableObject
    {
        [ObservableProperty] private string _fileName = "";
        [ObservableProperty] private string _imagePath = "";
        [ObservableProperty] private bool _isSelected = false;
        [ObservableProperty] private System.Windows.Media.ImageSource? _thumbnail;
        [ObservableProperty] private bool _isUserAdded = false;
    }
    public partial class BitFlagItem : ObservableObject
    {
        [ObservableProperty] private int _bitIndex;
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private bool _isChecked;
    }

    public partial class CustomAttachItem : ObservableObject
    {
        [ObservableProperty] private string _attachId = "";
        [ObservableProperty] private string _attachLength = "";
        [ObservableProperty] private string _attachData = "";
    }

    public partial class TextDownlinkMessage : ObservableObject
    {
        [ObservableProperty] private string _time = "";
        [ObservableProperty] private string _content = "";
        [ObservableProperty] private byte _flag;

        /// <summary>原始文本字节，用于重编码切换时实时重解码</summary>
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();

        public bool IsEmergency => (Flag & 1) != 0;    // bit0: 紧急
        public bool IsNotification => (Flag & 2) != 0; // bit1: 通知
        public bool IsAdScreen => (Flag & 4) != 0;     // bit2: 显示器显示
        public bool IsTTS => (Flag & 8) != 0;          // bit3: TTS播读
        public bool IsCanFault => (Flag & 32) != 0;    // bit5: CAN故障码信息

        /// <summary>简短标志描述（气泡顶部 Badge）</summary>
        public string FlagsDescription
        {
            get
            {
                var list = new System.Collections.Generic.List<string>();
                if (IsEmergency)    list.Add("紧急");
                if (IsNotification) list.Add("通知");
                if (IsAdScreen)     list.Add("显示器");
                if (IsTTS)          list.Add("TTS播读");
                if (IsCanFault)     list.Add("CAN故障");
                if (list.Count == 0) list.Add("无特别标志");
                return string.Join(" | ", list);
            }
        }

        /// <summary>详细标志说明（气泡内容区逐行）</summary>
        public string DetailedFlagsText
        {
            get
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"标志字节: 0x{Flag:X2}  ({Convert.ToString(Flag, 2).PadLeft(8, '0')}b)");
                lines.Add($" bit0 紧急     :{(IsEmergency    ? "✔ 是" : "✘ 否")}");
                lines.Add($" bit1 通知     :{(IsNotification ? "✔ 是" : "✘ 否")}");
                lines.Add($" bit2 屏显     :{(IsAdScreen     ? "✔ 是" : "✘ 否")}");
                lines.Add($" bit3 TTS播读  :{(IsTTS          ? "✔ 是" : "✘ 否")}");
                lines.Add($" bit5 CAN故障  :{(IsCanFault     ? "✔ 是" : "✘ 否")}");
                return string.Join("\n", lines);
            }
            set { }
        }

        /// <summary>根据指定编码序号重新解码原始字节（0=UTF-8, 1=GBK, 2=HEX）</summary>
        public void RecodeContent(int encodingIndex)
        {
            if (RawBytes == null || RawBytes.Length == 0) return;
            try
            {
                Content = encodingIndex switch
                {
                    2 => BitConverter.ToString(RawBytes).Replace("-", " "),
                    1 => System.Text.Encoding.GetEncoding("GBK").GetString(RawBytes),
                    _ => System.Text.Encoding.UTF8.GetString(RawBytes)
                };
            }
            catch
            {
                Content = BitConverter.ToString(RawBytes).Replace("-", " ");
            }
        }
    }

    public class AppConfig
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 808;
        public System.Collections.Generic.List<string> ServerAddressHistory { get; set; } = new();
        public string TerminalPhoneNo { get; set; } = "13812345678";
        public string AuthCode { get; set; } = "123456";
        public bool UseJT808_2019 { get; set; } = true;
        public double Speed { get; set; } = 60;
        public int Direction { get; set; } = 90;
        public double Altitude { get; set; } = 100;
        public uint AlarmFlagValue { get; set; } = 0;
        public uint StatusFlagValue { get; set; } = 0;
        public int AutoReportInterval { get; set; } = 5;
        public System.Collections.Generic.List<CustomAttachItem> CustomAttachItems { get; set; } = new();
        public string BackgroundImagePath { get; set; } = "";
        public BackgroundEffectMode BackgroundEffectMode { get; set; } = BackgroundEffectMode.Translucent;
        public double BackgroundOpacity { get; set; } = 0.8;
        public ushort ProvinceId { get; set; } = 11;
        public ushort CityId { get; set; } = 1101;
        public string ManufacturerId { get; set; } = "TEST ";
        public string TerminalModel { get; set; } = "Model-1";
        public string TerminalId { get; set; } = "T000001";
        public byte PlateColor { get; set; } = 1;
        public string PlateNo { get; set; } = "京A88888";
        public string SimNumber { get; set; } = "13812345678";
        public string TerminalIMEI { get; set; } = "861234567890123";
        public string HardwareVersion { get; set; } = "V1.0.0";
        public string FirmwareVersion { get; set; } = "V1.0.0";
        public bool UseAppVersionAsFirmwareVersion { get; set; } = true;
        public int AnalyzerMode { get; set; } = 0;
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 800;
        public bool EnableTTSPlayback { get; set; } = true;
        public string SelectedTTSVoice { get; set; } = "";
        public int TextDownlinkEncodingIndex { get; set; } = 0;

        // Standard Attachments
        public bool Enable0x01 { get; set; } = true;
        public uint Mileage0x01 { get; set; } = 0;
        public bool Enable0x02 { get; set; } = false;
        public ushort Oil0x02 { get; set; } = 0;
        public bool Enable0x03 { get; set; } = true;
        public ushort Speed0x03 { get; set; } = 0;
        public bool Enable0x04 { get; set; } = false;
        public ushort AlarmEventId0x04 { get; set; } = 0;
        public bool Enable0x25 { get; set; } = false;
        public uint ExtVehicleSignal0x25 { get; set; } = 0;
        public bool Enable0x2A { get; set; } = false;
        public ushort IOStatus0x2A { get; set; } = 0;
        public bool Enable0x2B { get; set; } = false;
        public ushort AnalogAD0 { get; set; } = 0;
        public ushort AnalogAD1 { get; set; } = 0;
        public bool Enable0x30 { get; set; } = true;
        public byte NetworkSignal0x30 { get; set; } = 31;
        public bool Enable0x31 { get; set; } = true;
        public byte GNSSCount0x31 { get; set; } = 15;

        // Simulation Parameters
        public bool EnableMileageSimulation { get; set; } = false;
        public bool Sync0x03SpeedWithMainSpeed { get; set; } = true;
        public bool EnableOilConsumption { get; set; } = false;
        public double OilConsumptionRate { get; set; } = 8.0;
        public bool EnableNetworkSignalFluctuation { get; set; } = false;
        public bool EnableGNSSFluctuation { get; set; } = false;
    }

    public class WorkStateConfig
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 808;
        public string TerminalPhoneNo { get; set; } = "13812345678";
        public string AuthCode { get; set; } = "123456";
        public bool UseJT808_2019 { get; set; } = true;
        public double Speed { get; set; } = 60;
        public int Direction { get; set; } = 90;
        public double Altitude { get; set; } = 100;
        public uint AlarmFlagValue { get; set; } = 0;
        public uint StatusFlagValue { get; set; } = 0;
        public int AutoReportInterval { get; set; } = 5;
        public System.Collections.Generic.List<CustomAttachItem> CustomAttachItems { get; set; } = new();
        public ushort ProvinceId { get; set; } = 11;
        public ushort CityId { get; set; } = 1101;
        public string ManufacturerId { get; set; } = "TEST ";
        public string TerminalModel { get; set; } = "Model-1";
        public string TerminalId { get; set; } = "T000001";
        public byte PlateColor { get; set; } = 1;
        public string PlateNo { get; set; } = "京A88888";
        public string SimNumber { get; set; } = "13812345678";
        public string TerminalIMEI { get; set; } = "861234567890123";
        public string HardwareVersion { get; set; } = "V1.0.0";
        public string FirmwareVersion { get; set; } = "V1.0.0";
        public bool UseAppVersionAsFirmwareVersion { get; set; } = true;
    }

    public partial class MainViewModel : ObservableObject, IDisposable, IAsyncDisposable, TerminalSimulation.PluginBase.IPluginContext
    {
        private readonly IConnectionSession _networkClient;
        private readonly JT808Manager _protocolManager;
        private readonly IAppLogger _appLogger;
        private readonly ISerialPortService _serialPortService;
        private readonly ITtsService _ttsService;
        private readonly ILocationSimulationService _locationSimulationService;
        private readonly Services.AtomicJsonConfigStore<AppConfig> _configStore;
        private readonly Channel<byte[]> _protocolQueue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        private readonly CancellationTokenSource _protocolProcessorCts = new();
        private readonly Task _protocolProcessorTask;
        private int _disposeState;

        partial void OnBackgroundImagePathChanged(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                BackgroundImageSource = null;
            }
            else if (value.StartsWith("pack://embedded/"))
            {
                var resName = value.Substring("pack://embedded/".Length);
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = stream;
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    BackgroundImageSource = bmp;
                }
            }
            else
            {
                try
                {
                    var fullPath = value;
                    if (!System.IO.Path.IsPathRooted(value))
                    {
                        fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
                    }
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(fullPath);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    BackgroundImageSource = bmp;
                }
                catch { BackgroundImageSource = null; }
            }
        }

        partial void OnChatEncodingIndexChanged(int value)
        {
            foreach (var msg in PassthroughMessages)
            {
                if (msg.RawData != null && msg.RawData.Length > 0)
                {
                    msg.Content = DecodePassthroughData(msg.RawData, value);
                }
            }
        }

        partial void OnTextDownlinkEncodingIndexChanged(int value)
        {
            UpdateSerializerEncoding();
            // 实时重解码已有消息
            foreach (var msg in TextDownlinkMessages)
            {
                msg.RecodeContent(value);
            }
        }

        private void UpdateSerializerEncoding()
        {
            if (_protocolManager != null)
            {
                // 协议栈编码：HEX模式仍用UTF-8做网络解析，只有UTF-8/GBK影响协议栈
                _protocolManager.Encoding = TextDownlinkEncodingIndex == 1
                    ? System.Text.Encoding.GetEncoding("GBK")
                    : System.Text.Encoding.UTF8;
            }
        }

        partial void OnPassthroughEncodingIndexChanged(int value)
        {
            if (value == 2 && !string.IsNullOrEmpty(PassthroughInputText))
            {
                var sanitized = SanitizeHex(PassthroughInputText);
                if (sanitized != PassthroughInputText)
                {
                    PassthroughInputText = sanitized;
                }
            }
            ValidateHexInput();
        }

        partial void OnPassthroughInputTextChanged(string value)
        {
            if (PassthroughEncodingIndex == 2)
            {
                var sanitized = SanitizeHex(value);
                if (sanitized != value)
                {
                    PassthroughInputText = sanitized;
                    return;
                }
            }
            ValidateHexInput();
        }

        private string SanitizeHex(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder();
            foreach (var c in input)
            {
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                {
                    sb.Append(char.ToUpper(c));
                }
            }
            return sb.ToString();
        }

        private void ValidateHexInput()
        {
            if (PassthroughEncodingIndex == 2 && !string.IsNullOrEmpty(PassthroughInputText))
            {
                IsHexInputInvalid = PassthroughInputText.Length % 2 != 0;
            }
            else
            {
                IsHexInputInvalid = false;
            }
        }

        private string DecodePassthroughData(byte[] data, int encodingIndex)
        {
            if (data == null || data.Length == 0) return "";
            if (encodingIndex == 2) // HEX
            {
                return data.ToHexString();
            }
            else
            {
                try
                {
                    var encoding = encodingIndex == 0 ? System.Text.Encoding.GetEncoding("GBK") : System.Text.Encoding.UTF8;
                    return encoding.GetString(data);
                }
                catch (Exception ex)
                {
                    return $"[解码失败: {ex.Message}]";
                }
            }
        }

        public string AppTitle => $"车载定位终端模拟系统 (JT808) {AppVersionInfo.FullVersion}";
        public MainViewModel()
        {
            _appLogger = new AppLogger();
            _serialPortService = new SerialPortService();
            _serialPortService.DataReceived += data => _ = HandleSerialDataReceivedAsync(data);
            _ttsService = new SystemTtsService();
            _locationSimulationService = new LocationSimulationService();
            InitializeFlags();
            _configStore = new Services.AtomicJsonConfigStore<AppConfig>(ConfigFile, warning => Log("配置", warning));
            _protocolManager = new JT808Manager();
            _networkClient = new TerminalConnectionSession();
            _networkClient.DataReceived += NetworkClient_OnDataReceived;
            _networkClient.Error += ex => Log("网络", ex.Message);
            _networkClient.Disconnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    Log("系统", "连接已断开");
                });
            };

            _protocolProcessorTask = ProcessProtocolQueueAsync(_protocolProcessorCts.Token);
            
            UpdateVideoChannels(VideoChannelCount);
            UpdateSerializerEncoding();

            CustomAttachItems.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (CustomAttachItem item in e.NewItems)
                    {
                        item.PropertyChanged += CustomAttachItem_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (CustomAttachItem item in e.OldItems)
                    {
                        item.PropertyChanged -= CustomAttachItem_PropertyChanged;
                    }
                }
                if (!_isLoadingConfig)
                {
                    SaveConfigDebounced();
                }
            };

            LoadConfig();
            InitializeTTSVoices();
            LoadRegions();
            InitThemeImages();
            RefreshSerialPorts();

            // Load Plugins
            InitializePlugins();

            // 监听属性变化并保存配置
            this.PropertyChanged += (s, e) =>
            {
                if (_isLoadingConfig) return;

                if (e.PropertyName == nameof(ServerIp) ||
                    e.PropertyName == nameof(ServerPort) ||
                    e.PropertyName == nameof(TerminalPhoneNo) ||
                    e.PropertyName == nameof(AuthCode) ||
                    e.PropertyName == nameof(UseJT808_2019) ||
                    e.PropertyName == nameof(Speed) ||
                    e.PropertyName == nameof(Direction) ||
                    e.PropertyName == nameof(CustomAttachItems) ||
                    e.PropertyName == nameof(BackgroundImagePath) ||
                    e.PropertyName == nameof(BackgroundEffectMode) ||
                    e.PropertyName == nameof(BackgroundOpacity) ||
                    e.PropertyName == nameof(ProvinceIdInput) ||
                    e.PropertyName == nameof(CityIdInput) ||
                    e.PropertyName == nameof(ManufacturerId) ||
                    e.PropertyName == nameof(TerminalModel) ||
                    e.PropertyName == nameof(TerminalId) ||
                    e.PropertyName == nameof(PlateColor) ||
                    e.PropertyName == nameof(PlateNo) ||
                    e.PropertyName == nameof(SimNumber) ||
                    e.PropertyName == nameof(TerminalIMEI) ||
                    e.PropertyName == nameof(HardwareVersion) ||
                    e.PropertyName == nameof(FirmwareVersion) ||
                    e.PropertyName == nameof(UseAppVersionAsFirmwareVersion) ||
                    e.PropertyName == nameof(ConfigWindowWidth) ||
                    e.PropertyName == nameof(ConfigWindowHeight) ||
                    e.PropertyName == nameof(Enable0x01) ||
                    e.PropertyName == nameof(Mileage0x01) ||
                    e.PropertyName == nameof(Enable0x02) ||
                    e.PropertyName == nameof(Oil0x02) ||
                    e.PropertyName == nameof(Enable0x03) ||
                    e.PropertyName == nameof(Speed0x03) ||
                    e.PropertyName == nameof(Enable0x04) ||
                    e.PropertyName == nameof(AlarmEventId0x04) ||
                    e.PropertyName == nameof(Enable0x25) ||
                    e.PropertyName == nameof(ExtVehicleSignal0x25) ||
                    e.PropertyName == nameof(Enable0x2A) ||
                    e.PropertyName == nameof(IOStatus0x2A) ||
                    e.PropertyName == nameof(Enable0x2B) ||
                    e.PropertyName == nameof(AnalogAD0) ||
                    e.PropertyName == nameof(AnalogAD1) ||
                    e.PropertyName == nameof(Enable0x30) ||
                    e.PropertyName == nameof(NetworkSignal0x30) ||
                    e.PropertyName == nameof(Enable0x31) ||
                    e.PropertyName == nameof(GNSSCount0x31) ||
                    e.PropertyName == nameof(EnableMileageSimulation) ||
                    e.PropertyName == nameof(Sync0x03SpeedWithMainSpeed) ||
                    e.PropertyName == nameof(EnableOilConsumption) ||
                    e.PropertyName == nameof(OilConsumptionRate) ||
                    e.PropertyName == nameof(EnableNetworkSignalFluctuation) ||
                    e.PropertyName == nameof(EnableGNSSFluctuation) ||
                    e.PropertyName == nameof(EnableTTSPlayback) ||
                    e.PropertyName == nameof(SelectedTTSVoice) ||
                    e.PropertyName == nameof(TextDownlinkEncodingIndex))
                {
                    SaveConfigDebounced();
                }
            };
            
            _simulationTimer = new System.Threading.Timer(SimulationTimerCallback, null, 1000, 1000);
        }

        private System.Threading.Timer? _simulationTimer;
        private double _mileageAccumulator = 0;
        private double _oilAccumulator = 0;
        private readonly Random _rand = new Random();

        private void SimulationTimerCallback(object? state)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => SimulationTimerCallback(state));
                return;
            }

            // Mileage Simulation
            if (EnableMileageSimulation)
            {
                // Speed is km/h, mileage is 1/10km
                // distance per second in km = Speed / 3600
                // distance per second in 1/10km = (Speed / 3600) * 10
                double increment = (Speed / 3600.0) * 10.0;
                _mileageAccumulator += increment;
                if (_mileageAccumulator >= 1.0)
                {
                    uint added = (uint)Math.Floor(_mileageAccumulator);
                    Mileage0x01 += added;
                    _mileageAccumulator -= added;
                }
            }

            // Speed Sync
            if (Sync0x03SpeedWithMainSpeed)
            {
                Speed0x03 = (ushort)(Speed * 10);
            }

            // Global Video Traffic Update
            long currentTotalBytes = 0;
            bool isAnyStreaming = false;

            foreach (var ch in VideoChannels)
            {
                ch.UpdateTraffic();
                currentTotalBytes += ch.CurrentPusherBytes;
                if (ch.IsStreaming) isAnyStreaming = true;
            }

            if (!isAnyStreaming)
            {
                TotalVideoTrafficText = "0 KB/s | 总计 0 MB";
                _lastGlobalTrafficUpdateTime = DateTime.MinValue;
                _lastTotalVideoBytes = 0;
            }
            else
            {
                var now = DateTime.Now;
                if (_lastGlobalTrafficUpdateTime == DateTime.MinValue)
                {
                    _lastGlobalTrafficUpdateTime = now;
                    _lastTotalVideoBytes = currentTotalBytes;
                    TotalVideoTrafficText = "计算中...";
                }
                else
                {
                    double seconds = (now - _lastGlobalTrafficUpdateTime).TotalSeconds;
                    if (seconds > 0)
                    {
                        double speedBps = (currentTotalBytes - _lastTotalVideoBytes) / seconds;
                        
                        string speedText = "";
                        if (speedBps < 1024) speedText = $"{speedBps:F0} B/s";
                        else if (speedBps < 1024 * 1024) speedText = $"{speedBps / 1024:F1} KB/s";
                        else speedText = $"{speedBps / (1024 * 1024):F1} MB/s";

                        double totalMb = currentTotalBytes / (1024.0 * 1024.0);
                        string totalText = currentTotalBytes < 1024 * 1024 ? $"{currentTotalBytes / 1024.0:F1} KB" : $"{totalMb:F1} MB";

                        TotalVideoTrafficText = $"{speedText} | 总计 {totalText}";
                    }

                    _lastGlobalTrafficUpdateTime = now;
                    _lastTotalVideoBytes = currentTotalBytes;
                }
            }

            // Oil Simulation
            if (EnableOilConsumption)
            {
                // Speed is km/h, OilConsumptionRate is L/100km
                // consumed L per hour = Speed * OilConsumptionRate / 100
                // consumed L per sec = (Speed * OilConsumptionRate / 100) / 3600
                // consumed 1/10L per sec = (Speed * OilConsumptionRate / 100) / 3600 * 10
                double oilIncrement = (Speed * OilConsumptionRate / 100.0) / 3600.0 * 10.0;
                _oilAccumulator += oilIncrement;
                if (_oilAccumulator >= 1.0)
                {
                    ushort consumed = (ushort)Math.Floor(_oilAccumulator);
                    if (Oil0x02 >= consumed)
                        Oil0x02 -= consumed;
                    else
                        Oil0x02 = 0;
                    _oilAccumulator -= consumed;
                }
            }

            // Fluctuation
            if (EnableNetworkSignalFluctuation)
            {
                if (_rand.NextDouble() < 0.3) // 30% chance to fluctuate each second
                {
                    int delta = _rand.Next(-1, 2);
                    int newVal = NetworkSignal0x30 + delta;
                    if (newVal < 0) newVal = 0;
                    if (newVal > 31) newVal = 31;
                    NetworkSignal0x30 = (byte)newVal;
                }
            }

            if (EnableGNSSFluctuation)
            {
                if (_rand.NextDouble() < 0.3)
                {
                    int delta = _rand.Next(-1, 2);
                    int newVal = GNSSCount0x31 + delta;
                    if (newVal < 0) newVal = 0;
                    if (newVal > 30) newVal = 30; // Max GNSS sats typical
                    GNSSCount0x31 = (byte)newVal;
                }
            }
        }

        private void CustomAttachItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_isLoadingConfig)
            {
                SaveConfigDebounced();
            }
        }

        private readonly string ConfigFile = System.IO.Path.Combine(TerminalSimulation.Wpf.Helpers.PathHelper.ExeDir, "config.json");

        private void InitializeFlags()
        {
            string[] alarmNames = { "紧急报警", "超速报警", "疲劳驾驶", "危险预警", "GNSS模块发生故障", "GNSS天线未接或被剪断", "GNSS天线短路", "终端主电源欠压", "终端主电源掉电", "终端LCD或显示器故障", "TTS模块故障", "摄像头故障", "道路运输证IC卡模块故障", "超速预警", "疲劳驾驶预警", "违规行驶报警", "胎压预警", "右转盲区异常报警", "当天累计驾驶超时", "超时停车", "进出区域", "进出路线", "路段行驶时间不足/过长", "路线偏离报警", "车辆VSS故障", "车辆油量异常", "车辆被盗", "车辆非法点火", "车辆非法位移", "碰撞预警", "侧翻预警", "保留" };
            for (int i = 0; i < alarmNames.Length; i++)
            {
                var item = new BitFlagItem { BitIndex = i, Name = $"[bit{i}]{alarmNames[i]}", IsChecked = false };
                item.PropertyChanged += (s, e) => { if (!_isLoadingConfig && e.PropertyName == nameof(BitFlagItem.IsChecked)) SaveConfigDebounced(); };
                AlarmFlags.Add(item);
            }

            string[] statusNames = { "ACC开", "已定位", "南纬", "西经", "停运状态", "经纬度已加密", "保留bit6", "保留bit7", "半载(bit8)", "满载(bit9)", "油路断开", "电路断开", "车门加锁", "前门开", "中门开", "后门开", "驾驶席门开", "自定义门开", "使用GPS卫星", "使用北斗卫星", "使用GLONASS卫星", "使用Galileo卫星", "车辆行驶" };
            for (int i = 0; i < statusNames.Length; i++)
            {
                var item = new BitFlagItem { BitIndex = i, Name = $"[bit{i}]{statusNames[i]}", IsChecked = false };
                item.PropertyChanged += (s, e) => { if (!_isLoadingConfig && e.PropertyName == nameof(BitFlagItem.IsChecked)) SaveConfigDebounced(); };
                StatusFlags.Add(item);
            }
        }

        private void SetFlagsFromValue(ObservableCollection<BitFlagItem> flags, uint value)
        {
            foreach (var flag in flags)
            {
                flag.IsChecked = (value & (1u << flag.BitIndex)) != 0;
            }
        }

        private uint GetValueFromFlags(ObservableCollection<BitFlagItem> flags)
        {
            uint val = 0;
            foreach (var flag in flags)
            {
                if (flag.IsChecked) val |= (1u << flag.BitIndex);
            }
            return val;
        }

        [ObservableProperty] private string _serverIp = "127.0.0.1";
        [ObservableProperty] private int _serverPort = 808;
        
        public ObservableCollection<string> ServerAddressHistory { get; } = new();
        [ObservableProperty] private string _serverAddressInput = "127.0.0.1:808";

        partial void OnServerAddressInputChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            int lastColon = value.LastIndexOf(':');
            if (lastColon > 0 && lastColon < value.Length - 1 && int.TryParse(value.Substring(lastColon + 1), out int port))
            {
                ServerIp = value.Substring(0, lastColon);
                ServerPort = port;
            }
            else
            {
                ServerIp = value;
                ServerPort = 808;
            }
        }
        [ObservableProperty] private string _terminalPhoneNo = "13812345678";
        [ObservableProperty] private string _authCode = "123456";
        [ObservableProperty] private int _audioCodecIndex = 0;
        [ObservableProperty] private bool _isConnected = false;

        [ObservableProperty] private string _provinceIdInput = "11";
        [ObservableProperty] private string _cityIdInput = "1101";
        [ObservableProperty] private string _manufacturerId = "TEST ";
        [ObservableProperty] private string _terminalModel = "Model-1";
        [ObservableProperty] private string _terminalId = "T000001";
        [ObservableProperty] private byte _plateColor = 1;
        [ObservableProperty] private string _plateNo = "京A88888";
        [ObservableProperty] private string _simNumber = "13812345678";
        [ObservableProperty] private string _terminalIMEI = "861234567890123";
        [ObservableProperty] private string _hardwareVersion = "V1.0.0";
        [ObservableProperty] private string _firmwareVersion = "V1.0.0";
        [ObservableProperty] private bool _useAppVersionAsFirmwareVersion = true;

        public ObservableCollection<PlateColorItem> PlateColorList { get; } = new ObservableCollection<PlateColorItem>
        {
            new PlateColorItem { Value = 1, Name = "1 - 蓝色" },
            new PlateColorItem { Value = 2, Name = "2 - 黄色" },
            new PlateColorItem { Value = 3, Name = "3 - 黑色" },
            new PlateColorItem { Value = 4, Name = "4 - 白色" },
            new PlateColorItem { Value = 5, Name = "5 - 绿色" },
            new PlateColorItem { Value = 9, Name = "9 - 其他" },
            new PlateColorItem { Value = 0, Name = "0 - 未上牌" }
        };
        public ObservableCollection<string> ProvinceList { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> CityList { get; } = new ObservableCollection<string>();

        private System.Collections.Generic.List<RegionNode> _allRegions = new();

        private void LoadRegions()
        {
            try
            {
                var jsonPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Regions.json");
                string json = string.Empty;
                if (System.IO.File.Exists(jsonPath))
                {
                    json = System.IO.File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                }
                else
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using var stream = assembly.GetManifestResourceStream("TerminalSimulation.Wpf.Regions.json");
                    if (stream != null)
                    {
                        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                        json = reader.ReadToEnd();
                    }
                }

                if (!string.IsNullOrEmpty(json))
                {
                    _allRegions = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<RegionNode>>(json) ?? new();
                    ProvinceList.Clear();
                    foreach (var prov in _allRegions)
                    {
                        ProvinceList.Add($"{prov.code} {prov.name}");
                    }
                    
                    // Trigger city load for initial province
                    if (!string.IsNullOrEmpty(ProvinceIdInput))
                    {
                        OnProvinceIdInputChanged(ProvinceIdInput);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("系统", $"加载行政区划失败: {ex.Message}");
            }
        }

        partial void OnProvinceIdInputChanged(string value)
        {
            if (_isLoadingConfig) return;
            CityList.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                var code = value.Split(' ')[0];
                var prov = _allRegions.FirstOrDefault(x => x.code == code);
                if (prov != null && prov.children != null)
                {
                    foreach (var city in prov.children)
                    {
                        CityList.Add($"{city.code} {city.name}");
                    }
                    if (CityList.Count > 0 && string.IsNullOrEmpty(CityIdInput) || !CityList.Contains(CityIdInput))
                    {
                        CityIdInput = CityList[0];
                    }
                }
            }
        }

        private ushort ParseProvinceId(string? input, ushort defVal)
        {
            var codeStr = input?.Split(' ')[0];
            if (string.IsNullOrEmpty(codeStr)) return defVal;
            if (codeStr.Length >= 2) codeStr = codeStr.Substring(0, 2);
            return ushort.TryParse(codeStr, out var pid) ? pid : defVal;
        }

        private ushort ParseCityId(string? input, ushort defVal)
        {
            var codeStr = input?.Split(' ')[0];
            if (string.IsNullOrEmpty(codeStr)) return defVal;
            if (codeStr.Length == 6) codeStr = codeStr.Substring(2, 4);
            else if (codeStr.Length > 4) codeStr = codeStr.Substring(codeStr.Length - 4);
            return ushort.TryParse(codeStr, out var cid) ? cid : defVal;
        }

        [ObservableProperty] private double _latitude = 39.9042;
        [ObservableProperty] private double _longitude = 116.4074;
        [ObservableProperty] private double _speed = 60.5;
        [ObservableProperty] private int _direction = 90;
        [ObservableProperty] private double _altitude = 100;
        
        [ObservableProperty] private bool _useJT808_2019 = true;

        [ObservableProperty] private string _backgroundImagePath = "";
        [ObservableProperty] private System.Windows.Media.ImageSource? _backgroundImageSource;
        [ObservableProperty] private BackgroundEffectMode _backgroundEffectMode = BackgroundEffectMode.Translucent;
        [ObservableProperty] private double _backgroundOpacity = 0.8;


        public int BackgroundEffectModeIndex
        {
            get => (int)BackgroundEffectMode;
            set
            {
                if (value >= 0 && value <= 3)
                {
                    BackgroundEffectMode = (BackgroundEffectMode)value;
                    OnPropertyChanged(nameof(BackgroundEffectModeIndex));
                }
            }
        }

        public int ProtocolVersionIndex
        {
            get => UseJT808_2019 ? 1 : 0;
            set
            {
                UseJT808_2019 = value == 1;
            }
        }

        partial void OnUseJT808_2019Changed(bool value)
        {
            OnPropertyChanged(nameof(ProtocolVersionIndex));
        }

        [ObservableProperty] private int _autoReportInterval = 5;
        [ObservableProperty] private bool _isAutoReporting = false;
        private System.Threading.CancellationTokenSource? _autoReportCts;
        private Task? _autoReportTask;

        [ObservableProperty] private bool _autoScrollLogs = true;
        [ObservableProperty] private ObservableCollection<LogMessageItem> _logMessages = new();
        [ObservableProperty] private bool _isUtilitiesVisible = false;
        

        [ObservableProperty] private string _analyzerInputHex = "";
        public ObservableCollection<AnalyzerNode> AnalyzerResultTree { get; } = new();
        
        [ObservableProperty] private int _analyzerMode = 0;
        public int[] IntRange1to36 { get; } = System.Linq.Enumerable.Range(1, 36).ToArray();

        [ObservableProperty] private int _videoChannelCount = 4;
        public ObservableCollection<VideoChannelItem> VideoChannels { get; } = new();

        [ObservableProperty] private string _totalVideoTrafficText = "0 KB/s | 总计 0 MB";
        private long _lastTotalVideoBytes = 0;
        private DateTime _lastGlobalTrafficUpdateTime = DateTime.MinValue;

        [RelayCommand]
        private async Task StopAllPushStreamsAsync()
        {
            await Task.WhenAll(VideoChannels.Select(channel => channel.StopPushingAsync()));
        }

        partial void OnVideoChannelCountChanged(int value)
        {
            UpdateVideoChannels(value);
        }

        private void UpdateVideoChannels(int count)
        {
            if (count < 1) count = 1;
            if (count > 36) count = 36;
            
            while (VideoChannels.Count > count)
            {
                var item = VideoChannels.Last();
                item.Dispose();
                VideoChannels.Remove(item);
            }
            while (VideoChannels.Count < count)
            {
                VideoChannels.Add(new VideoChannelItem((byte)(VideoChannels.Count + 1), Log));
            }
        }

        [ObservableProperty] private double _configWindowWidth = 1200;
        [ObservableProperty] private double _configWindowHeight = 800;

        // Standard Attach Properties
        [ObservableProperty] private bool _enable0x01 = true;
        [ObservableProperty] private uint _mileage0x01 = 0;
        [ObservableProperty] private bool _enable0x02 = false;
        [ObservableProperty] private ushort _oil0x02 = 0;
        [ObservableProperty] private bool _enable0x03 = true;
        [ObservableProperty] private ushort _speed0x03 = 0;
        [ObservableProperty] private bool _enable0x04 = false;
        [ObservableProperty] private ushort _alarmEventId0x04 = 0;
        [ObservableProperty] private bool _enable0x25 = false;
        [ObservableProperty] private uint _extVehicleSignal0x25 = 0;

        partial void OnExtVehicleSignal0x25Changed(uint value)
        {
            OnPropertyChanged(nameof(ExtVehSigLowBeam));
            OnPropertyChanged(nameof(ExtVehSigHighBeam));
            OnPropertyChanged(nameof(ExtVehSigRightTurn));
            OnPropertyChanged(nameof(ExtVehSigLeftTurn));
            OnPropertyChanged(nameof(ExtVehSigBrake));
            OnPropertyChanged(nameof(ExtVehSigReverse));
            OnPropertyChanged(nameof(ExtVehSigFogLight));
            OnPropertyChanged(nameof(ExtVehSigOutlineLight));
            OnPropertyChanged(nameof(ExtVehSigHorn));
            OnPropertyChanged(nameof(ExtVehSigAC));
            OnPropertyChanged(nameof(ExtVehSigNeutral));
            OnPropertyChanged(nameof(ExtVehSigRetarder));
            OnPropertyChanged(nameof(ExtVehSigABS));
            OnPropertyChanged(nameof(ExtVehSigHeater));
            OnPropertyChanged(nameof(ExtVehSigClutch));
        }

        public bool ExtVehSigLowBeam
        {
            get => (ExtVehicleSignal0x25 & (1U << 0)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 0);
                else ExtVehicleSignal0x25 &= ~(1U << 0);
            }
        }

        public bool ExtVehSigHighBeam
        {
            get => (ExtVehicleSignal0x25 & (1U << 1)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 1);
                else ExtVehicleSignal0x25 &= ~(1U << 1);
            }
        }

        public bool ExtVehSigRightTurn
        {
            get => (ExtVehicleSignal0x25 & (1U << 2)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 2);
                else ExtVehicleSignal0x25 &= ~(1U << 2);
            }
        }

        public bool ExtVehSigLeftTurn
        {
            get => (ExtVehicleSignal0x25 & (1U << 3)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 3);
                else ExtVehicleSignal0x25 &= ~(1U << 3);
            }
        }

        public bool ExtVehSigBrake
        {
            get => (ExtVehicleSignal0x25 & (1U << 4)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 4);
                else ExtVehicleSignal0x25 &= ~(1U << 4);
            }
        }

        public bool ExtVehSigReverse
        {
            get => (ExtVehicleSignal0x25 & (1U << 5)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 5);
                else ExtVehicleSignal0x25 &= ~(1U << 5);
            }
        }

        public bool ExtVehSigFogLight
        {
            get => (ExtVehicleSignal0x25 & (1U << 6)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 6);
                else ExtVehicleSignal0x25 &= ~(1U << 6);
            }
        }

        public bool ExtVehSigOutlineLight
        {
            get => (ExtVehicleSignal0x25 & (1U << 7)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 7);
                else ExtVehicleSignal0x25 &= ~(1U << 7);
            }
        }

        public bool ExtVehSigHorn
        {
            get => (ExtVehicleSignal0x25 & (1U << 8)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 8);
                else ExtVehicleSignal0x25 &= ~(1U << 8);
            }
        }

        public bool ExtVehSigAC
        {
            get => (ExtVehicleSignal0x25 & (1U << 9)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 9);
                else ExtVehicleSignal0x25 &= ~(1U << 9);
            }
        }

        public bool ExtVehSigNeutral
        {
            get => (ExtVehicleSignal0x25 & (1U << 10)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 10);
                else ExtVehicleSignal0x25 &= ~(1U << 10);
            }
        }

        public bool ExtVehSigRetarder
        {
            get => (ExtVehicleSignal0x25 & (1U << 11)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 11);
                else ExtVehicleSignal0x25 &= ~(1U << 11);
            }
        }

        public bool ExtVehSigABS
        {
            get => (ExtVehicleSignal0x25 & (1U << 12)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 12);
                else ExtVehicleSignal0x25 &= ~(1U << 12);
            }
        }

        public bool ExtVehSigHeater
        {
            get => (ExtVehicleSignal0x25 & (1U << 13)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 13);
                else ExtVehicleSignal0x25 &= ~(1U << 13);
            }
        }

        public bool ExtVehSigClutch
        {
            get => (ExtVehicleSignal0x25 & (1U << 14)) != 0;
            set
            {
                if (value) ExtVehicleSignal0x25 |= (1U << 14);
                else ExtVehicleSignal0x25 &= ~(1U << 14);
            }
        }

        [ObservableProperty] private bool _enable0x2A = false;
        [ObservableProperty] private ushort _iOStatus0x2A = 0;

        partial void OnIOStatus0x2AChanged(ushort value)
        {
            OnPropertyChanged(nameof(IOStatusDeepSleep));
            OnPropertyChanged(nameof(IOStatusSleep));
            OnPropertyChanged(nameof(IOStatusGPIO2));
            OnPropertyChanged(nameof(IOStatusGPIO3));
            OnPropertyChanged(nameof(IOStatusGPIO4));
            OnPropertyChanged(nameof(IOStatusGPIO5));
            OnPropertyChanged(nameof(IOStatusGPIO6));
            OnPropertyChanged(nameof(IOStatusGPIO7));
            OnPropertyChanged(nameof(IOStatusGPIO8));
            OnPropertyChanged(nameof(IOStatusGPIO9));
            OnPropertyChanged(nameof(IOStatusGPIO10));
            OnPropertyChanged(nameof(IOStatusGPIO11));
            OnPropertyChanged(nameof(IOStatusGPIO12));
            OnPropertyChanged(nameof(IOStatusGPIO13));
            OnPropertyChanged(nameof(IOStatusGPIO14));
            OnPropertyChanged(nameof(IOStatusGPIO15));
        }

        public bool IOStatusDeepSleep
        {
            get => (IOStatus0x2A & (1U << 0)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 0);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 0));
            }
        }

        public bool IOStatusSleep
        {
            get => (IOStatus0x2A & (1U << 1)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 1);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 1));
            }
        }

        public bool IOStatusGPIO2
        {
            get => (IOStatus0x2A & (1U << 2)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 2);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 2));
            }
        }

        public bool IOStatusGPIO3
        {
            get => (IOStatus0x2A & (1U << 3)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 3);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 3));
            }
        }

        public bool IOStatusGPIO4
        {
            get => (IOStatus0x2A & (1U << 4)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 4);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 4));
            }
        }

        public bool IOStatusGPIO5
        {
            get => (IOStatus0x2A & (1U << 5)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 5);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 5));
            }
        }

        public bool IOStatusGPIO6
        {
            get => (IOStatus0x2A & (1U << 6)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 6);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 6));
            }
        }

        public bool IOStatusGPIO7
        {
            get => (IOStatus0x2A & (1U << 7)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 7);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 7));
            }
        }

        public bool IOStatusGPIO8
        {
            get => (IOStatus0x2A & (1U << 8)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 8);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 8));
            }
        }

        public bool IOStatusGPIO9
        {
            get => (IOStatus0x2A & (1U << 9)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 9);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 9));
            }
        }

        public bool IOStatusGPIO10
        {
            get => (IOStatus0x2A & (1U << 10)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 10);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 10));
            }
        }

        public bool IOStatusGPIO11
        {
            get => (IOStatus0x2A & (1U << 11)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 11);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 11));
            }
        }

        public bool IOStatusGPIO12
        {
            get => (IOStatus0x2A & (1U << 12)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 12);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 12));
            }
        }

        public bool IOStatusGPIO13
        {
            get => (IOStatus0x2A & (1U << 13)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 13);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 13));
            }
        }

        public bool IOStatusGPIO14
        {
            get => (IOStatus0x2A & (1U << 14)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 14);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 14));
            }
        }

        public bool IOStatusGPIO15
        {
            get => (IOStatus0x2A & (1U << 15)) != 0;
            set
            {
                if (value) IOStatus0x2A |= (ushort)(1U << 15);
                else IOStatus0x2A &= (ushort)(0xFFFF ^ (1U << 15));
            }
        }

        [ObservableProperty] private bool _enable0x2B = false;
        [ObservableProperty] private ushort _analogAD0 = 0;
        [ObservableProperty] private ushort _analogAD1 = 0;
        [ObservableProperty] private bool _enable0x30 = true;
        [ObservableProperty] private byte _networkSignal0x30 = 31;
        [ObservableProperty] private bool _enable0x31 = true;
        [ObservableProperty] private byte _gNSSCount0x31 = 15;

        // Simulation Properties
        [ObservableProperty] private bool _enableMileageSimulation = false;
        [ObservableProperty] private bool _sync0x03SpeedWithMainSpeed = true;
        [ObservableProperty] private bool _enableOilConsumption = false;
        [ObservableProperty] private double _oilConsumptionRate = 8.0;
        [ObservableProperty] private bool _enableNetworkSignalFluctuation = false;
        [ObservableProperty] private bool _enableGNSSFluctuation = false;
        [ObservableProperty] private bool _enableTTSPlayback = true;
        [ObservableProperty] private string _selectedTTSVoice = "";
        public ObservableCollection<string> InstalledTTSVoices { get; } = new();
        public ObservableCollection<TextDownlinkMessage> TextDownlinkMessages { get; } = new();

        public ObservableCollection<AnalyzerTableRow> AnalyzerResultTable { get; } = new();

        private System.Threading.CancellationTokenSource? _pathSimulationCts;
        private Task? _pathSimulationTask;
        [ObservableProperty] private bool _isPathSimulating = false;
        
        [ObservableProperty] private bool _isSettingsOpen = false;

        [RelayCommand]
        private void CloseSettings()
        {
            IsSettingsOpen = false;
        }

        public Action<double, double>? OnMapCarMoved { get; set; }
        public Action? OnSimulationFinished { get; set; }

        public ObservableCollection<BitFlagItem> AlarmFlags { get; } = new ObservableCollection<BitFlagItem>();
        public ObservableCollection<BitFlagItem> StatusFlags { get; } = new ObservableCollection<BitFlagItem>();
        public ObservableCollection<CustomAttachItem> CustomAttachItems { get; } = new ObservableCollection<CustomAttachItem>();
        
        public ObservableCollection<PassthroughMessage> PassthroughMessages { get; } = new ObservableCollection<PassthroughMessage>();
        private const int MaxPassthroughMessages = 1000;
        private const int MaxTextDownlinkMessages = 1000;

        private static void TrimOldest<T>(ObservableCollection<T> collection, int maximum)
        {
            while (collection.Count > maximum)
            {
                collection.RemoveAt(0);
            }
        }
        [ObservableProperty] private string _passthroughInputText = "";
        [ObservableProperty] private string _passthroughTypeHex = "00";
        [ObservableProperty] private int _passthroughEncodingIndex = 0; // 0: GBK, 1: UTF-8, 2: HEX
        [ObservableProperty] private int _chatEncodingIndex = 0; // 0: GBK, 1: UTF-8, 2: HEX
        [ObservableProperty] private int _textDownlinkEncodingIndex = 0; // 0: UTF-8, 1: GBK
        [ObservableProperty] private bool _isHexInputInvalid = false;

        // Serial Port Properties
        public ObservableCollection<string> SerialPorts { get; } = new ObservableCollection<string>();
        [ObservableProperty] private string? _selectedSerialPort;
        public ObservableCollection<int> BaudRates { get; } = new ObservableCollection<int> { 4800, 9600, 19200, 38400, 57600, 115200 };
        [ObservableProperty] private int _selectedBaudRate = 9600;
        [ObservableProperty] private bool _isSerialPortOpen = false;
        [ObservableProperty] private string _serialPortBtnText = "打开串口";

        [ObservableProperty] private string _terminalStatusText = "未连接";
        [ObservableProperty] private string _terminalStatusColor = "Gray";

        [ObservableProperty] private int _heartbeatInterval = 30;
        [ObservableProperty] private bool _isHeartbeatDisabled = false;
        private System.Threading.CancellationTokenSource? _heartbeatCts;
        private Task? _heartbeatTask;


        public ObservableCollection<ThemeImageItem> ThemeImages { get; } = new ObservableCollection<ThemeImageItem>();

        private void InitializeTTSVoices()
        {
            try
            {
                foreach (var voiceName in _ttsService.GetInstalledVoices())
                {
                    InstalledTTSVoices.Add(voiceName);
                }
                
                if (InstalledTTSVoices.Count > 0)
                {
                    if (string.IsNullOrEmpty(SelectedTTSVoice) || !InstalledTTSVoices.Contains(SelectedTTSVoice))
                    {
                        SelectedTTSVoice = InstalledTTSVoices[0];
                    }
                }
            }
            catch (Exception ex)
            {
                Log("系统", $"初始化语音朗读引擎失败: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ReplayTTSAsync(TextDownlinkMessage? msg)
        {
            if (msg == null || !EnableTTSPlayback) return;
            try { await _ttsService.SpeakAsync(msg.Content, SelectedTTSVoice); }
            catch (Exception ex) { Log("系统", $"TTS重播失败: {ex.Message}"); }
        }

        private void InitThemeImages()
        {
            try
            {
                var themeDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
                if (!System.IO.Directory.Exists(themeDir))
                {
                    System.IO.Directory.CreateDirectory(themeDir);
                }
                
                LoadThemeImages();
            }
            catch (Exception ex)
            {
                Log("系统", $"初始化主题目录失败: {ex.Message}");
            }
        }

        public void LoadThemeImages()
        {
            try
            {
                var themeDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
                ThemeImages.Clear();
                ThemeImages.Add(new ThemeImageItem { FileName = "无背景", ImagePath = "", IsSelected = string.IsNullOrEmpty(BackgroundImagePath), IsUserAdded = false });

                // Load embedded resources
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames().Where(x => x.StartsWith("TerminalSimulation.Wpf.Themes.", StringComparison.OrdinalIgnoreCase));
                foreach (var resName in resourceNames)
                {
                    var ext = System.IO.Path.GetExtension(resName).ToLower();
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                    {
                        var fileName = resName.Substring("TerminalSimulation.Wpf.Themes.".Length);
                        var item = new ThemeImageItem
                        {
                            FileName = fileName,
                            ImagePath = "pack://embedded/" + resName,
                            IsUserAdded = false,
                            IsSelected = BackgroundImagePath == "pack://embedded/" + resName
                        };
                        try
                        {
                            using var stream = assembly.GetManifestResourceStream(resName);
                            if (stream != null)
                            {
                                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                                bmp.BeginInit();
                                bmp.StreamSource = stream;
                                bmp.DecodePixelWidth = 200;
                                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                item.Thumbnail = bmp;
                            }
                        }
                        catch (Exception ex) { _appLogger.Error("主题", $"读取缩略图失败: {resName}", ex); }
                        ThemeImages.Add(item);
                    }
                }

                // Load user external resources
                if (System.IO.Directory.Exists(themeDir))
                {
                    var files = System.IO.Directory.GetFiles(themeDir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                    foreach (var file in files)
                    {
                        var relativePath = System.IO.Path.Combine("Themes", System.IO.Path.GetFileName(file));
                        var item = new ThemeImageItem
                        {
                            FileName = System.IO.Path.GetFileName(file),
                            ImagePath = relativePath,
                            IsSelected = BackgroundImagePath == relativePath,
                            IsUserAdded = true
                        };
                        
                        try
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(file);
                            bmp.DecodePixelWidth = 200;
                            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            item.Thumbnail = bmp;
                        }
                        catch (Exception ex) { _appLogger.Error("主题", $"读取缩略图失败: {file}", ex); }
                        
                        ThemeImages.Add(item);
                    }
                }
            }
            catch (Exception ex) { _appLogger.Error("主题", "扫描主题图片失败", ex); }
        }

        [RelayCommand]
        private void AddThemeImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png|所有文件|*.*",
                Title = "选择背景图片"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var themeDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
                    if (!System.IO.Directory.Exists(themeDir)) System.IO.Directory.CreateDirectory(themeDir);
                    
                    var fileName = System.IO.Path.GetFileName(dialog.FileName);
                    var destFile = System.IO.Path.Combine(themeDir, fileName);
                    
                    if (dialog.FileName != destFile)
                    {
                        System.IO.File.Copy(dialog.FileName, destFile, true);
                    }
                    
                    LoadThemeImages();
                    var relativePath = System.IO.Path.Combine("Themes", fileName);
                    SelectThemeImage(ThemeImages.FirstOrDefault(x => x.ImagePath == relativePath));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"添加图片失败: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void SelectThemeImage(ThemeImageItem? item)
        {
            if (item != null)
            {
                foreach (var i in ThemeImages) i.IsSelected = false;
                item.IsSelected = true;
                BackgroundImagePath = item.ImagePath;
            }
        }

        [RelayCommand]
        private void DeleteThemeImage(ThemeImageItem? item)
        {
            if (item != null && item.IsUserAdded)
            {
                try
                {
                    var fullPath = item.ImagePath;
                    if (!System.IO.Path.IsPathRooted(fullPath))
                    {
                        fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fullPath);
                    }
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                    if (item.IsSelected)
                    {
                        BackgroundImagePath = "";
                    }
                    LoadThemeImages();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void AddCustomAttach()
        {
            var newItem = new CustomAttachItem { AttachId = "", AttachLength = "", AttachData = "" };
            CustomAttachItems.Add(newItem);
        }

        [RelayCommand]
        private void RemoveCustomAttach(CustomAttachItem item)
        {
            if (item != null)
            {
                CustomAttachItems.Remove(item);
            }
        }

        [RelayCommand]
        private void ToggleAutoReport()
        {
            ConsoleLogger.LogAction("切换自动位置汇报", $"当前状态={IsAutoReporting} -> 目标状态={!IsAutoReporting}, 间隔={AutoReportInterval}秒");
            if (IsAutoReporting)
            {
                _autoReportCts?.Cancel();
                IsAutoReporting = false;
                Log("系统", "已停止自动位置汇报");
            }
            else
            {
                if (AutoReportInterval < 1)
                {
                    Log("系统", "自动汇报间隔不能小于1秒");
                    return;
                }

                IsAutoReporting = true;
                _autoReportCts = new System.Threading.CancellationTokenSource();
                var token = _autoReportCts.Token;
                Log("系统", $"已开启自动位置汇报，间隔 {AutoReportInterval} 秒");

                _autoReportTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            await ReportLocationAsync();
                            await Task.Delay(TimeSpan.FromSeconds(AutoReportInterval), token);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception ex) { Log("自动汇报", $"后台任务异常: {ex.Message}"); }
                    finally
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() => IsAutoReporting = false));
                    }
                }, token);
            }
        }
        private void StartHeartbeatLoop()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = new System.Threading.CancellationTokenSource();
            var token = _heartbeatCts.Token;

            _heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(HeartbeatInterval > 0 ? HeartbeatInterval : 30), token);

                        if (!IsHeartbeatDisabled && IsConnected)
                        {
                            var header = new JT808Header
                            {
                                MsgId = 0x0002,
                                TerminalPhoneNo = TerminalPhoneNo,
                                MsgNum = 0
                            };
                            var package = new JT808Package { Header = header };
                            await SendPackageAsync<JT808_0x0002>(package);
                            Log("系统", "已自动发送 0x0002 心跳");
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) { Log("心跳", $"后台任务异常: {ex.Message}"); }
            }, token);
        }

        [RelayCommand]
        private async Task SendPassthroughAsync()
        {
            if (!IsConnected)
            {
                Log("系统", "请先连接服务器");
                return;
            }

            if (string.IsNullOrEmpty(PassthroughInputText))
            {
                Log("系统", "请输入要透传的内容");
                return;
            }

            if (PassthroughEncodingIndex == 2)
            {
                if (PassthroughInputText.Length % 2 != 0)
                {
                    Log("系统", "HEX 模式下输入内容长度必须为偶数，发送已拒绝");
                    return;
                }
            }

            try
            {
                byte ptType = Convert.ToByte(PassthroughTypeHex, 16);
                byte[] ptData;

                if (PassthroughEncodingIndex == 2) // HEX
                {
                    ptData = PassthroughInputText.ToHexBytes();
                }
                else
                {
                    var encoding = PassthroughEncodingIndex == 0 ? System.Text.Encoding.GetEncoding("GBK") : System.Text.Encoding.UTF8;
                    ptData = encoding.GetBytes(PassthroughInputText);
                }

                var header = new JT808Header
                {
                    MsgId = 0x0900,
                    TerminalPhoneNo = TerminalPhoneNo,
                    MsgNum = 4
                };

                var body = new JT808_0x0900
                {
                    PassthroughType = ptType,
                    PassthroughData = ptData
                };

                var package = new JT808Package
                {
                    Header = header,
                    Bodies = body
                };

                await SendPackageAsync<JT808_0x0900>(package);

                // If serial port is open, also write data to it
                bool writeToSerialSuccess = false;
                if (_serialPortService.IsOpen)
                {
                    try
                    {
                        _serialPortService.Write(ptData);
                        writeToSerialSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        Log("系统", $"发送数据到串口失败: {ex.Message}");
                    }
                }

                // Add to UI List
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PassthroughMessages.Add(new PassthroughMessage
                    {
                        IsFromServer = false,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        TypeHex = ptType.ToString("X2"),
                        RawData = ptData,
                        Content = DecodePassthroughData(ptData, ChatEncodingIndex),
                        Label = writeToSerialSuccess ? "[终端 -> 平台/串口]" : "[终端 -> 平台]"
                    });
                    TrimOldest(PassthroughMessages, MaxPassthroughMessages);
                    PassthroughInputText = "";
                });
            }
            catch (Exception ex)
            {
                Log("系统", $"发送透传失败: {ex.Message}");
            }
        }

        [RelayCommand]
        public void RefreshSerialPorts()
        {
            try
            {
                var ports = _serialPortService.GetPortNames();
                SerialPorts.Clear();
                foreach (var port in ports)
                {
                    SerialPorts.Add(port);
                }
                if (SerialPorts.Count > 0)
                {
                    if (string.IsNullOrEmpty(SelectedSerialPort) || !SerialPorts.Contains(SelectedSerialPort))
                    {
                        SelectedSerialPort = SerialPorts[0];
                    }
                }
                else
                {
                    SelectedSerialPort = null;
                }
            }
            catch (Exception ex)
            {
                Log("系统", $"获取串口列表失败: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleSerialPort()
        {
            if (IsSerialPortOpen)
            {
                CloseSerialPort();
            }
            else
            {
                OpenSerialPort();
            }
        }

        private void OpenSerialPort()
        {
            if (string.IsNullOrEmpty(SelectedSerialPort))
            {
                Log("系统", "无可用串口，打开失败");
                return;
            }

            try
            {
                _serialPortService.Open(SelectedSerialPort, SelectedBaudRate);

                IsSerialPortOpen = true;
                SerialPortBtnText = "关闭串口";
                Log("系统", $"串口已打开: {SelectedSerialPort} (波特率: {SelectedBaudRate})");
            }
            catch (Exception ex)
            {
                Log("系统", $"打开串口 {SelectedSerialPort} 失败: {ex.Message}");
                CloseSerialPort();
            }
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPortService.IsOpen)
                {
                    _serialPortService.Close();
                    Log("系统", "串口已关闭");
                }
            }
            catch (Exception ex)
            {
                Log("系统", $"关闭串口异常: {ex.Message}");
            }
            finally
            {
                IsSerialPortOpen = false;
                SerialPortBtnText = "打开串口";
            }
        }

        private async Task HandleSerialDataReceivedAsync(byte[] data)
        {
            try
            {
                byte ptType = 0;
                _ = byte.TryParse(PassthroughTypeHex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out ptType);

                string contentStr;
                if (PassthroughEncodingIndex == 2) // HEX
                {
                    contentStr = data.ToHexString();
                }
                else
                {
                    var encoding = PassthroughEncodingIndex == 0 ? System.Text.Encoding.GetEncoding("GBK") : System.Text.Encoding.UTF8;
                    contentStr = encoding.GetString(data);
                }

                // Forward to server if connected
                if (IsConnected)
                {
                    var header = new JT808Header
                    {
                        MsgId = 0x0900,
                        TerminalPhoneNo = TerminalPhoneNo,
                        MsgNum = 5
                    };

                    var body = new JT808_0x0900
                    {
                        PassthroughType = ptType,
                        PassthroughData = data
                    };

                    var package = new JT808Package
                    {
                        Header = header,
                        Bodies = body
                    };

                    await SendPackageAsync<JT808_0x0900>(package);
                }

                // Show in UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PassthroughMessages.Add(new PassthroughMessage
                    {
                        IsFromServer = false,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        TypeHex = ptType.ToString("X2"),
                        RawData = data,
                        Content = DecodePassthroughData(data, ChatEncodingIndex),
                        Label = IsConnected ? "[串口 -> 平台]" : "[串口接收]"
                    });
                    TrimOldest(PassthroughMessages, MaxPassthroughMessages);
                });
            }
            catch (Exception ex)
            {
                Log("系统", $"转发串口数据失败: {ex.Message}");
            }
        }

        public void StopPathSimulation()
        {
            if (IsPathSimulating)
            {
                _pathSimulationCts?.Cancel();
                IsPathSimulating = false;
                Log("系统", "路径模拟行驶已手动中断");
            }
        }

        public void StartPathSimulation(List<GeoPoint> path)
        {
            if (path == null || path.Count < 2)
            {
                Log("系统", "路径点太少，无法模拟");
                return;
            }

            if (IsPathSimulating)
            {
                _pathSimulationCts?.Cancel();
            }

            if (AutoReportInterval < 1)
            {
                Log("系统", "模拟汇报间隔不能小于1秒");
                return;
            }

            _pathSimulationCts = new System.Threading.CancellationTokenSource();
            var token = _pathSimulationCts.Token;
            IsPathSimulating = true;
            Log("系统", "开始路径模拟行驶");

            _pathSimulationTask = Task.Run(async () =>
            {
                try
                {
                    await _locationSimulationService.RunAsync(path, () => Speed, (point, bearing) =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            Latitude = Math.Round(point.Lat, 6);
                            Longitude = Math.Round(point.Lng, 6);
                            Direction = bearing;
                            OnMapCarMoved?.Invoke(point.Lat, point.Lng);
                        });
                    }, token);
                    if (!token.IsCancellationRequested)
                    {
                        Log("系统", "路径模拟行驶已到达终点");
                        Application.Current?.Dispatcher?.Invoke(() => OnSimulationFinished?.Invoke());
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Log("系统", $"路径模拟出错: {ex.Message}");
                    Application.Current?.Dispatcher?.Invoke(() => OnSimulationFinished?.Invoke());
                }
                finally
                {
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() => IsPathSimulating = false));
                }
            }, token);
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

            _autoReportCts?.Cancel();
            _heartbeatCts?.Cancel();
            _pathSimulationCts?.Cancel();

            foreach (var tab in OpenedUtilityTabs.ToArray())
            {
                if (tab.Content.DataContext is IDisposable disposable) disposable.Dispose();
            }
            OpenedUtilityTabs.Clear();
            Converters.PluginToContentConverter.DisposeCachedContent();

            var backgroundTasks = new[] { _autoReportTask, _heartbeatTask, _pathSimulationTask }
                .Where(task => task != null).Cast<Task>().ToArray();
            try { await Task.WhenAll(backgroundTasks); }
            catch (OperationCanceledException) { }

            await Task.WhenAll(VideoChannels.Select(channel => channel.DisposeAsync().AsTask()));
            await _networkClient.DisposeAsync();
            _protocolQueue.Writer.TryComplete();
            try { await _protocolProcessorTask; }
            catch (OperationCanceledException) { }
            _protocolProcessorCts.Cancel();
            _protocolProcessorCts.Dispose();
            CloseSerialPort();

            _autoReportCts?.Dispose();
            _heartbeatCts?.Dispose();
            _pathSimulationCts?.Dispose();
            _autoReportCts = null;
            _heartbeatCts = null;
            _pathSimulationCts = null;
            _autoReportTask = null;
            _heartbeatTask = null;
            _pathSimulationTask = null;

            _simulationTimer?.Dispose();
            _simulationTimer = null;

            // Force save any pending config change immediately on dispose
            if (_saveTimer != null)
            {
                _saveTimer.Dispose();
                _saveTimer = null;
                SaveConfig();
            }
            Task pendingSave;
            lock (_saveLock) { pendingSave = _lastConfigSaveTask; }
            await pendingSave;
            GC.SuppressFinalize(this);
        }

        

        public void LogMessage(string source, string message)
        {
            Log(source, message);
        }

        
    }

    public class GeoPoint
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    public partial class PassthroughMessage : ObservableObject
    {
        public bool IsFromServer { get; set; }
        public string Time { get; set; } = "";
        public string TypeHex { get; set; } = "";
        
        [ObservableProperty]
        private string _content = "";
        
        public string Label { get; set; } = "";
        public byte[] RawData { get; set; } = Array.Empty<byte>();
    }

}
