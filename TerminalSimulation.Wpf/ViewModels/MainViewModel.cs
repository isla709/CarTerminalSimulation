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

    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly TerminalNetworkClient _networkClient;
        private readonly JT808Manager _protocolManager;

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
            InitializeFlags();
            _networkClient = new TerminalNetworkClient();
            _networkClient.OnDataReceived += NetworkClient_OnDataReceived;
            _networkClient.OnDisconnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    Log("系统", "连接已断开");
                });
            };

            _protocolManager = new JT808Manager();
            
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

        private readonly string ConfigFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

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

        private bool _isLoadingConfig = false;
        private readonly object _configLock = new object();
        private System.Threading.Timer? _saveTimer;
        private readonly object _saveLock = new object();

        private void SaveConfigDebounced()
        {
            lock (_saveLock)
            {
                if (_saveTimer == null)
                {
                    _saveTimer = new System.Threading.Timer(SaveTimerCallback, null, 500, System.Threading.Timeout.Infinite);
                }
                else
                {
                    _saveTimer.Change(500, System.Threading.Timeout.Infinite);
                }
            }
        }

        private void SaveTimerCallback(object? state)
        {
            SaveConfig();
        }

        private void LoadConfig()
        {
            lock (_configLock)
            {
                _isLoadingConfig = true;
                try
                {
                    string tempFile = ConfigFile + ".tmp";
                    if (!File.Exists(ConfigFile) && File.Exists(tempFile))
                    {
                        try
                        {
                            File.Move(tempFile, ConfigFile);
                        }
                        catch { }
                    }

                    if (File.Exists(ConfigFile))
                    {
                        var json = File.ReadAllText(ConfigFile);
                        var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
                        if (config != null)
                        {
                            ServerIp = config.ServerIp;
                            ServerPort = config.ServerPort;
                            TerminalPhoneNo = config.TerminalPhoneNo;
                            AuthCode = config.AuthCode;
                            UseJT808_2019 = config.UseJT808_2019;
                            Speed = config.Speed;
                            Direction = config.Direction;
                            Altitude = config.Altitude;
                            AutoReportInterval = config.AutoReportInterval;
                            ProvinceIdInput = config.ProvinceId.ToString();
                            CityIdInput = config.CityId.ToString();
                            ManufacturerId = config.ManufacturerId ?? "TEST ";
                            TerminalModel = config.TerminalModel ?? "Model-1";
                            TerminalId = config.TerminalId ?? "T000001";
                            PlateColor = config.PlateColor;
                            PlateNo = config.PlateNo ?? "京A88888";
                            SimNumber = config.SimNumber ?? "13812345678";
                            TerminalIMEI = config.TerminalIMEI ?? "861234567890123";
                            HardwareVersion = config.HardwareVersion ?? "V1.0.0";
                            FirmwareVersion = config.FirmwareVersion ?? "V1.0.0";
                            UseAppVersionAsFirmwareVersion = config.UseAppVersionAsFirmwareVersion;
                            AnalyzerMode = config.AnalyzerMode;
                            ConfigWindowWidth = config.WindowWidth > 400 ? config.WindowWidth : 1200;
                            ConfigWindowHeight = config.WindowHeight > 300 ? config.WindowHeight : 800;

                            Enable0x01 = config.Enable0x01;
                            Mileage0x01 = config.Mileage0x01;
                            Enable0x02 = config.Enable0x02;
                            Oil0x02 = config.Oil0x02;
                            Enable0x03 = config.Enable0x03;
                            Speed0x03 = config.Speed0x03;
                            Enable0x04 = config.Enable0x04;
                            AlarmEventId0x04 = config.AlarmEventId0x04;
                            Enable0x25 = config.Enable0x25;
                            ExtVehicleSignal0x25 = config.ExtVehicleSignal0x25;
                            Enable0x2A = config.Enable0x2A;
                            IOStatus0x2A = config.IOStatus0x2A;
                            Enable0x2B = config.Enable0x2B;
                            AnalogAD0 = config.AnalogAD0;
                            AnalogAD1 = config.AnalogAD1;
                            Enable0x30 = config.Enable0x30;
                            NetworkSignal0x30 = config.NetworkSignal0x30;
                            Enable0x31 = config.Enable0x31;
                            GNSSCount0x31 = config.GNSSCount0x31;

                            EnableMileageSimulation = config.EnableMileageSimulation;
                            Sync0x03SpeedWithMainSpeed = config.Sync0x03SpeedWithMainSpeed;
                            EnableOilConsumption = config.EnableOilConsumption;
                            OilConsumptionRate = config.OilConsumptionRate;
                            EnableNetworkSignalFluctuation = config.EnableNetworkSignalFluctuation;
                            EnableGNSSFluctuation = config.EnableGNSSFluctuation;
                            EnableTTSPlayback = config.EnableTTSPlayback;
                            SelectedTTSVoice = config.SelectedTTSVoice ?? "";
                            TextDownlinkEncodingIndex = config.TextDownlinkEncodingIndex;

                            var bgPath = config.BackgroundImagePath;
                            var bgEffect = config.BackgroundEffectMode;

                            if (!string.IsNullOrEmpty(bgPath))
                            {
                                if (bgPath.StartsWith("pack://embedded/"))
                                {
                                    // Embedded resource
                                }
                                else
                                {
                                    var fileName = System.IO.Path.GetFileName(bgPath);
                                    var relativePath = System.IO.Path.Combine("Themes", fileName);
                                    var fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                                    if (System.IO.File.Exists(fullPath))
                                    {
                                        bgPath = relativePath;
                                    }
                                    else if (System.IO.Path.IsPathRooted(bgPath) && System.IO.File.Exists(bgPath))
                                    {
                                        try
                                        {
                                            var themeDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
                                            if (!System.IO.Directory.Exists(themeDir)) System.IO.Directory.CreateDirectory(themeDir);
                                            var destFile = System.IO.Path.Combine(themeDir, fileName);
                                            System.IO.File.Copy(bgPath, destFile, true);
                                            bgPath = relativePath;
                                        }
                                        catch
                                        {
                                            // Keep as is if copy fails
                                        }
                                    }
                                    else
                                    {
                                        bgPath = "";
                                        bgEffect = BackgroundEffectMode.None;
                                    }
                                }
                            }

                            BackgroundImagePath = bgPath;
                            BackgroundEffectMode = bgEffect;
                            BackgroundOpacity = config.BackgroundOpacity;
                            
                            SetFlagsFromValue(AlarmFlags, config.AlarmFlagValue);
                            SetFlagsFromValue(StatusFlags, config.StatusFlagValue);

                            if (config.CustomAttachItems != null)
                            {
                                CustomAttachItems.Clear();
                                foreach (var item in config.CustomAttachItems)
                                {
                                    CustomAttachItems.Add(item);
                                }
                            }
                        }
                    }
                    else
                    {
                        SaveConfig(); // Create default config file if it does not exist
                    }
                }
                catch (Exception ex)
                {
                    Log("系统", $"加载配置失败: {ex.Message}");
                }
                finally
                {
                    _isLoadingConfig = false;
                }
            }
        }

        private class LocationReportSnapshot
        {
            public string TerminalPhoneNo { get; set; } = "";
            public uint AlarmFlag { get; set; }
            public uint StatusFlag { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public double Altitude { get; set; }
            public double Speed { get; set; }
            public int Direction { get; set; }
            public System.Collections.Generic.List<CustomAttachItem> CustomAttachItems { get; set; } = new();

            public bool Enable0x01 { get; set; }
            public uint Mileage0x01 { get; set; }
            public bool Enable0x02 { get; set; }
            public ushort Oil0x02 { get; set; }
            public bool Enable0x03 { get; set; }
            public ushort Speed0x03 { get; set; }
            public bool Enable0x04 { get; set; }
            public ushort AlarmEventId0x04 { get; set; }
            public bool Enable0x25 { get; set; }
            public uint ExtVehicleSignal0x25 { get; set; }
            public bool Enable0x2A { get; set; }
            public ushort IOStatus0x2A { get; set; }
            public bool Enable0x2B { get; set; }
            public ushort AnalogAD0 { get; set; }
            public ushort AnalogAD1 { get; set; }
            public bool Enable0x30 { get; set; }
            public byte NetworkSignal0x30 { get; set; }
            public bool Enable0x31 { get; set; }
            public byte GNSSCount0x31 { get; set; }
        }

        private LocationReportSnapshot CaptureLocationReportSnapshot()
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => CaptureLocationReportSnapshotInternal());
            }
            return CaptureLocationReportSnapshotInternal();
        }

        private LocationReportSnapshot CaptureLocationReportSnapshotInternal()
        {
            return new LocationReportSnapshot
            {
                TerminalPhoneNo = TerminalPhoneNo,
                AlarmFlag = GetValueFromFlags(AlarmFlags),
                StatusFlag = GetValueFromFlags(StatusFlags),
                Latitude = Latitude,
                Longitude = Longitude,
                Altitude = Altitude,
                Speed = Speed,
                Direction = Direction,
                CustomAttachItems = CustomAttachItems.Select(x => new CustomAttachItem
                {
                    AttachId = x.AttachId,
                    AttachLength = x.AttachLength,
                    AttachData = x.AttachData
                }).ToList(),
                
                Enable0x01 = Enable0x01,
                Mileage0x01 = Mileage0x01,
                Enable0x02 = Enable0x02,
                Oil0x02 = Oil0x02,
                Enable0x03 = Enable0x03,
                Speed0x03 = Speed0x03,
                Enable0x04 = Enable0x04,
                AlarmEventId0x04 = AlarmEventId0x04,
                Enable0x25 = Enable0x25,
                ExtVehicleSignal0x25 = ExtVehicleSignal0x25,
                Enable0x2A = Enable0x2A,
                IOStatus0x2A = IOStatus0x2A,
                Enable0x2B = Enable0x2B,
                AnalogAD0 = AnalogAD0,
                AnalogAD1 = AnalogAD1,
                Enable0x30 = Enable0x30,
                NetworkSignal0x30 = NetworkSignal0x30,
                Enable0x31 = Enable0x31,
                GNSSCount0x31 = GNSSCount0x31
            };
        }

        private AppConfig CaptureAppConfig()
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => CaptureAppConfigInternal());
            }
            return CaptureAppConfigInternal();
        }

        private AppConfig CaptureAppConfigInternal()
        {
            return new AppConfig
            {
                ServerIp = ServerIp,
                ServerPort = ServerPort,
                TerminalPhoneNo = TerminalPhoneNo,
                AuthCode = AuthCode,
                UseJT808_2019 = UseJT808_2019,
                Speed = Speed,
                Direction = Direction,
                Altitude = Altitude,
                AutoReportInterval = AutoReportInterval,
                AlarmFlagValue = GetValueFromFlags(AlarmFlags),
                StatusFlagValue = GetValueFromFlags(StatusFlags),
                CustomAttachItems = CustomAttachItems.Select(x => new CustomAttachItem
                {
                    AttachId = x.AttachId,
                    AttachLength = x.AttachLength,
                    AttachData = x.AttachData
                }).ToList(),
                BackgroundImagePath = BackgroundImagePath,
                BackgroundEffectMode = BackgroundEffectMode,
                BackgroundOpacity = BackgroundOpacity,
                ProvinceId = ParseProvinceId(ProvinceIdInput, 11),
                CityId = ParseCityId(CityIdInput, 1101),
                ManufacturerId = ManufacturerId,
                TerminalModel = TerminalModel,
                TerminalId = TerminalId,
                PlateColor = PlateColor,
                PlateNo = PlateNo,
                SimNumber = SimNumber,
                TerminalIMEI = TerminalIMEI,
                HardwareVersion = HardwareVersion,
                FirmwareVersion = FirmwareVersion,
                UseAppVersionAsFirmwareVersion = UseAppVersionAsFirmwareVersion,
                AnalyzerMode = AnalyzerMode,
                WindowWidth = ConfigWindowWidth,
                WindowHeight = ConfigWindowHeight,
                Enable0x01 = Enable0x01,
                Mileage0x01 = Mileage0x01,
                Enable0x02 = Enable0x02,
                Oil0x02 = Oil0x02,
                Enable0x03 = Enable0x03,
                Speed0x03 = Speed0x03,
                Enable0x04 = Enable0x04,
                AlarmEventId0x04 = AlarmEventId0x04,
                Enable0x25 = Enable0x25,
                ExtVehicleSignal0x25 = ExtVehicleSignal0x25,
                Enable0x2A = Enable0x2A,
                IOStatus0x2A = IOStatus0x2A,
                Enable0x2B = Enable0x2B,
                AnalogAD0 = AnalogAD0,
                AnalogAD1 = AnalogAD1,
                Enable0x30 = Enable0x30,
                NetworkSignal0x30 = NetworkSignal0x30,
                Enable0x31 = Enable0x31,
                GNSSCount0x31 = GNSSCount0x31,
                EnableMileageSimulation = EnableMileageSimulation,
                Sync0x03SpeedWithMainSpeed = Sync0x03SpeedWithMainSpeed,
                EnableOilConsumption = EnableOilConsumption,
                OilConsumptionRate = OilConsumptionRate,
                EnableNetworkSignalFluctuation = EnableNetworkSignalFluctuation,
                EnableGNSSFluctuation = EnableGNSSFluctuation,
                EnableTTSPlayback = EnableTTSPlayback,
                SelectedTTSVoice = SelectedTTSVoice,
                TextDownlinkEncodingIndex = TextDownlinkEncodingIndex
            };
        }

        private void SaveConfig()
        {
            var config = CaptureAppConfig();
            lock (_configLock)
            {
                int retries = 5;
                while (retries > 0)
                {
                    try
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var tempFile = ConfigFile + ".tmp";
                        File.WriteAllText(tempFile, json);
                        if (File.Exists(ConfigFile))
                        {
                            File.Delete(ConfigFile);
                        }
                        File.Move(tempFile, ConfigFile);
                        break; // Success!
                    }
                    catch (IOException) when (retries > 1)
                    {
                        retries--;
                        System.Threading.Thread.Sleep(50);
                    }
                    catch (Exception ex)
                    {
                        Log("系统", $"保存配置失败: {ex.Message}");
                        break;
                    }
                }
            }
        }

        [ObservableProperty] private string _serverIp = "127.0.0.1";
        [ObservableProperty] private int _serverPort = 808;
        [ObservableProperty] private string _terminalPhoneNo = "13812345678";
        [ObservableProperty] private string _authCode = "123456";
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

        [ObservableProperty] private bool _autoScrollLogs = true;
        [ObservableProperty] private ObservableCollection<LogMessageItem> _logMessages = new();
        [ObservableProperty] private bool _isAnalyzerVisible = false;
        [ObservableProperty] private string _analyzerInputHex = "";
        public ObservableCollection<AnalyzerNode> AnalyzerResultTree { get; } = new();
        
        [ObservableProperty] private int _analyzerMode = 0;
        public int[] IntRange1to36 { get; } = System.Linq.Enumerable.Range(1, 36).ToArray();

        [ObservableProperty] private int _videoChannelCount = 4;
        public ObservableCollection<VideoChannelItem> VideoChannels { get; } = new();

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
        [ObservableProperty] private string _passthroughInputText = "";
        [ObservableProperty] private string _passthroughTypeHex = "00";
        [ObservableProperty] private int _passthroughEncodingIndex = 0; // 0: GBK, 1: UTF-8, 2: HEX
        [ObservableProperty] private int _chatEncodingIndex = 0; // 0: GBK, 1: UTF-8, 2: HEX
        [ObservableProperty] private int _textDownlinkEncodingIndex = 0; // 0: UTF-8, 1: GBK
        [ObservableProperty] private bool _isHexInputInvalid = false;

        // Serial Port Properties
        private System.IO.Ports.SerialPort? _serialPort;
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


        public ObservableCollection<ThemeImageItem> ThemeImages { get; } = new ObservableCollection<ThemeImageItem>();

        private void InitializeTTSVoices()
        {
            try
            {
                using (var synth = new System.Speech.Synthesis.SpeechSynthesizer())
                {
                    foreach (var voice in synth.GetInstalledVoices())
                    {
                        if (voice.Enabled)
                        {
                            InstalledTTSVoices.Add(voice.VoiceInfo.Name);
                        }
                    }
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
        private void ReplayTTS(TextDownlinkMessage? msg)
        {
            if (msg == null || !EnableTTSPlayback) return;
            string textToSpeak = msg.Content;
            string voiceName = SelectedTTSVoice;
            Task.Run(() =>
            {
                try
                {
                    using var synth = new System.Speech.Synthesis.SpeechSynthesizer();
                    if (!string.IsNullOrEmpty(voiceName))
                        synth.SelectVoice(voiceName);
                    synth.Speak(textToSpeak);
                }
                catch (Exception ex)
                {
                    Log("系统", $"TTS重播失败: {ex.Message}");
                }
            });
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
                        catch { }
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
                        catch { }
                        
                        ThemeImages.Add(item);
                    }
                }
            }
            catch { }
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
        private void ClearLogs()
        {
            LogMessages.Clear();
            PassthroughMessages.Clear();
        }

        [RelayCommand]
        private void AnalyzeMessage()
        {
            try
            {
                AnalyzerResultTree.Clear();
                if (string.IsNullOrWhiteSpace(AnalyzerInputHex)) return;
                
                string hex = AnalyzerInputHex.Replace(" ", "").Replace("\r", "").Replace("\n", "");
                byte[] data = Convert.FromHexString(hex);
                
                string json = _protocolManager.Analyze(data);
                
                using (var doc = JsonDocument.Parse(json))
                {
                    var rootNode = ParseJsonElement("JT808 Package", doc.RootElement);
                    AnalyzerResultTree.Add(rootNode);
                    
                    AnalyzerResultTable.Clear();
                    int offset = 0;
                    TraverseJsonForTable("JT808 Package", doc.RootElement, ref offset);
                }
            }
            catch (Exception ex)
            {
                AnalyzerResultTree.Add(new AnalyzerNode { Name = "解析错误", Value = ex.Message });
            }
        }
        
        private AnalyzerNode ParseJsonElement(string name, JsonElement element)
        {
            var node = new AnalyzerNode { Name = TranslateKey(name) };
            
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    node.Children.Add(ParseJsonElement(prop.Name, prop.Value));
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    node.Children.Add(ParseJsonElement($"[{i}]", item));
                    i++;
                }
            }
            else
            {
                string rawValue = element.ToString() ?? "";
                node.Value = TranslateValue(name, rawValue);
            }
            
            return node;
        }

        private string TranslateKey(string key)
        {
            if (key.Contains("消息Id", StringComparison.OrdinalIgnoreCase)) return key.Replace("消息Id", "消息ID ", StringComparison.OrdinalIgnoreCase);
            if (key.Contains("车牌颜色")) return key.Replace("车牌颜色", "车牌颜色 ");
            
            return key switch
            {
                "MsgId" => "消息ID",
                "MsgNum" => "消息流水号",
                "TerminalPhoneNo" => "终端手机号",
                "Header" => "消息头",
                "MessageBodyProperty" => "消息体属性",
                "VersionFlag" => "版本标识",
                "Encrypt" => "加密方式",
                "DataLength" => "数据长度",
                "TerminalId" => "终端ID",
                "PlateColor" => "车牌颜色",
                "Bodies" => "消息体",
                "CheckCode" => "校验码",
                "JT808 Package" => "JT808 报文",
                _ => key
            };
        }

        private string TranslateMsgId(ushort msgId)
        {
            string hex = msgId.ToString("X4");
            string desc = hex switch
            {
                "0001" => "终端通用应答",
                "8001" => "平台通用应答",
                "0002" => "终端心跳",
                "8003" => "补传分包请求",
                "0100" => "终端注册",
                "8100" => "终端注册应答",
                "0102" => "终端鉴权",
                "0104" => "查询终端参数应答",
                "8103" => "设置终端参数",
                "8104" => "查询终端参数",
                "8105" => "终端控制",
                "8106" => "查询指定终端参数",
                "8107" => "查询终端属性",
                "0107" => "查询终端属性应答",
                "0108" => "终端升级结果通知",
                "0200" => "位置信息汇报",
                "0201" => "位置信息查询应答",
                "8201" => "位置信息查询",
                "8202" => "临时位置跟踪控制",
                "8203" => "人工确认报警消息",
                "8300" => "文本信息下发",
                "8301" => "事件设置",
                "0301" => "事件报告",
                "8302" => "提问下发",
                "0302" => "提问应答",
                "8303" => "信息点播菜单设置",
                "0303" => "信息点播/取消",
                "8304" => "信息服务",
                "8400" => "电话回拨",
                "8401" => "设置电话本",
                "8500" => "车辆控制",
                "0500" => "车辆控制应答",
                "8600" => "设置多边形区域",
                "8601" => "删除多边形区域",
                "8602" => "设置矩形区域",
                "8603" => "删除矩形区域",
                "8604" => "设置圆形区域",
                "8605" => "删除圆形区域",
                "8606" => "设置路线",
                "8607" => "删除路线",
                "8800" => "多媒体数据上传应答",
                "0800" => "多媒体事件信息上传",
                "0801" => "多媒体数据上传",
                "8801" => "摄像头立即拍摄命令",
                "0805" => "摄像头立即拍摄命令应答",
                "8802" => "存储多媒体数据检索",
                "0802" => "存储多媒体数据检索应答",
                "8803" => "存储多媒体数据上传",
                "8804" => "录音开始命令",
                "0900" => "数据上行透传",
                "8900" => "数据下行透传",
                "0901" => "数据压缩上报",
                "0A00" => "终端RSA公钥",
                "8A00" => "平台RSA公钥",
                _ => ""
            };
            return desc;
        }

        private string TranslateValue(string key, string value)
        {
            if (key.Contains("消息Id", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int msgId))
            {
                string desc = TranslateMsgId((ushort)msgId);
                return $"{msgId} {desc}".Trim();
            }
            else if (key.Contains("车牌颜色") && int.TryParse(value, out int colorId))
            {
                string colorDesc = colorId switch
                {
                    1 => "蓝色",
                    2 => "黄色",
                    3 => "黑色",
                    4 => "白色",
                    5 => "绿色",
                    9 => "其他",
                    _ => "未指定"
                };
                return $"{colorId} {colorDesc}";
            }

            return value;
        }

        private void TraverseJsonForTable(string name, JsonElement element, ref int offset)
        {
            if (name == "JT808 Package")
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        TraverseJsonForTable(prop.Name, prop.Value, ref offset);
                    }
                }
                return;
            }

            string hexData = "";
            string field = name;
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^\[([0-9A-Fa-f]+|bit[0-9~]+)\](.*)$");
            if (match.Success)
            {
                hexData = match.Groups[1].Value;
                field = match.Groups[2].Value.Trim();
            }

            field = TranslateKey(field).Replace(" ", "");
            string rawValue = element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array ? element.ToString() ?? "" : "";
            string result = TranslateValue(name, rawValue);

            string offsetStr = "";
            string lengthStr = "";
            string dataType = "";

            if (!string.IsNullOrEmpty(hexData))
            {
                if (hexData.StartsWith("bit"))
                {
                    offsetStr = "-";
                    lengthStr = "bit";
                    dataType = "BIT";
                }
                else
                {
                    offsetStr = offset.ToString();
                    int len = hexData.Length / 2;
                    lengthStr = len.ToString();
                    dataType = len switch
                    {
                        1 => "BYTE",
                        2 => "WORD",
                        4 => "DWORD",
                        _ => "BYTES"
                    };
                    offset += len;
                }
            }
            
            if (!string.IsNullOrEmpty(hexData) || (!string.IsNullOrEmpty(rawValue) && element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array))
            {
                if (element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array)
                {
                    AnalyzerResultTable.Add(new AnalyzerTableRow
                    {
                        Index = AnalyzerResultTable.Count,
                        Field = field,
                        HexData = hexData,
                        DataType = dataType,
                        OffsetStr = offsetStr,
                        LengthStr = lengthStr,
                        Result = result
                    });
                }
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    TraverseJsonForTable(prop.Name, prop.Value, ref offset);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    TraverseJsonForTable($"[{i}]", item, ref offset);
                    i++;
                }
            }
        }

        private void Log(string direction, string message)
        {
            // Forward to console logger
            if (direction == "发送")
            {
                ConsoleLogger.LogNetwork("发送", Array.Empty<byte>(), message);
            }
            else if (direction == "发送解析")
            {
                ConsoleLogger.LogNetwork("发送解析", Array.Empty<byte>(), message);
            }
            else if (direction == "接收")
            {
                ConsoleLogger.LogNetwork("接收", Array.Empty<byte>(), message);
            }
            else if (direction == "接收解析")
            {
                ConsoleLogger.LogNetwork("接收解析", Array.Empty<byte>(), message);
            }
            else if (direction == "系统")
            {
                ConsoleLogger.LogInfo(message);
            }
            else if (direction == "异常" || direction == "解析异常")
            {
                ConsoleLogger.LogError(direction, message);
            }
            else
            {
                ConsoleLogger.LogDebug(direction, message);
            }

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    bool isRaw = message.StartsWith("RAW: ");
                    string rawHex = "";
                    string displayMsg = message;

                    if (isRaw)
                    {
                        rawHex = message.Substring(5).Trim();
                        displayMsg = message; // Keep RAW: prefix for visual clarity, or you can strip it
                    }

                    var item = new LogMessageItem
                    {
                        TimestampStr = $"[{DateTime.Now:HH:mm:ss.fff}]",
                        DirectionStr = $"[{direction}]",
                        Message = displayMsg,
                        HasRaw = isRaw,
                        RawData = rawHex
                    };

                    LogMessages.Add(item);

                    // Keep only the last 2000 log items to prevent memory issues
                    if (LogMessages.Count > 2000)
                    {
                        LogMessages.RemoveAt(0);
                    }
                });
            }
        }

        private void RemoveErrorProperties(JsonNode node)
        {
            if (node is JsonObject jObj)
            {
                jObj.Remove("解析外部部未知附加信息报错");
                jObj.Remove("解析异常");
                foreach (var kvp in jObj.ToArray())
                {
                    if (kvp.Value != null) RemoveErrorProperties(kvp.Value);
                }
            }
            else if (node is JsonArray jArr)
            {
                foreach (var item in jArr)
                {
                    if (item != null) RemoveErrorProperties(item);
                }
            }
        }

        private void NetworkClient_OnDataReceived(byte[] data)
        {
            string hexStr = data.ToHexString();
            Log("接收", $"RAW: {hexStr}");

            try
            {
                var package = _protocolManager.Deserialize(data);
                string desc = TranslateMsgId(package.Header.MsgId);
                if (!string.IsNullOrEmpty(desc))
                {
                    Log("接收", $"{desc}(0x{package.Header.MsgId:X4})");
                }

                string analysis = _protocolManager.Analyze(data);
                
                // 格式化解析出的 JSON 以提高可读性，包含中文支持
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var jNode = JsonNode.Parse(analysis);
                    if (jNode != null)
                    {
                        RemoveErrorProperties(jNode);
                        analysis = jNode.ToJsonString(options);
                    }
                }
                catch { } // 如果不是标准 JSON 就不格式化

                Log("接收解析", $"\n{analysis}");

                // 自动获取注册应答 (0x8100) 中的鉴权码
                if (package.Header.MsgId == 0x8100 && package.Bodies is JT808_0x8100 registerResponse)
                {
                    if (registerResponse.JT808TerminalRegisterResult == JT808TerminalRegisterResult.success)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AuthCode = registerResponse.Code;
                            Log("系统", $"已自动获取鉴权码: {AuthCode}");
                            TerminalStatusText = "已注册，待鉴权";
                            TerminalStatusColor = "#2196F3"; // Blue
                        });
                    }
                    else
                    {
                        Log("系统", $"注册失败，错误码: {registerResponse.JT808TerminalRegisterResult}");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TerminalStatusText = $"注册失败: {registerResponse.JT808TerminalRegisterResult}";
                            TerminalStatusColor = "#F44336"; // Red
                        });
                    }
                }
                // 拦截查询终端属性 (0x8107)
                if (package.Header.MsgId == 0x8107)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var header = new JT808Header
                            {
                                MsgId = 0x0107,
                                TerminalPhoneNo = TerminalPhoneNo,
                                MsgNum = 1,
                            };

                            var body = new JT808_0x0107
                            {
                                TerminalType = 0,
                                MakerId = ManufacturerId,
                                TerminalModel = TerminalModel,
                                TerminalId = TerminalId,
                                Terminal_SIM_ICCID = SimNumber,
                                Terminal_Hardware_Version_Num = HardwareVersion,
                                Terminal_Firmware_Version_Num = UseAppVersionAsFirmwareVersion ? AppVersionInfo.FullVersion : FirmwareVersion,
                                GNSSModule = 1,
                                CommunicationModule = 1
                            };

                            var replyPackage = new JT808Package
                            {
                                Header = header,
                                Bodies = body
                            };

                            var version = UseJT808_2019 ? JT808Version.JTT2019 : JT808Version.JTT2013;
                            byte[] replyData = _protocolManager.Serialize(replyPackage, version);
                            await _networkClient.SendAsync(replyData);
                            Log("发送", $"自动应答 0x0107 查询终端属性");
                        }
                        catch (Exception ex)
                        {
                            Log("系统", $"发送 0x0107 应答异常: {ex.Message}");
                        }
                    });
                }
                
                // 拦截平台通用应答 (0x8001)
                if (package.Header.MsgId == 0x8001 && package.Bodies is JT808_0x8001 platformResponse)
                {
                    // 判断是否为终端鉴权 (0x0102) 的应答
                    if (platformResponse.AckMsgId == 0x0102)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (platformResponse.JT808PlatformResult == JT808.Protocol.Enums.JT808PlatformResult.succeed)
                            {
                                TerminalStatusText = "鉴权成功";
                                TerminalStatusColor = "#4CAF50"; // Green
                                Log("系统", "鉴权成功！");
                            }
                            else
                            {
                                TerminalStatusText = $"鉴权失败: {platformResponse.JT808PlatformResult}";
                                TerminalStatusColor = "#F44336"; // Red
                                Log("系统", $"鉴权失败: {platformResponse.JT808PlatformResult}");
                            }
                        });
                    }
                }
                
                // 拦截下行透传报文 (0x8900)
                if (package.Header.MsgId == 0x8900 && package.Bodies is JT808_0x8900 ptDown)
                {
                    // If serial port is open, write data to it
                    bool writeToSerialSuccess = false;
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        try
                        {
                            _serialPort.Write(ptDown.PassthroughData, 0, ptDown.PassthroughData.Length);
                            writeToSerialSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            Log("系统", $"接收数据写入串口失败: {ex.Message}");
                        }
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PassthroughMessages.Add(new PassthroughMessage
                        {
                            IsFromServer = true,
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            TypeHex = ptDown.PassthroughType.ToString("X2"),
                            RawData = ptDown.PassthroughData,
                            Content = DecodePassthroughData(ptDown.PassthroughData, ChatEncodingIndex),
                            Label = writeToSerialSuccess ? "[平台 -> 串口]" : "[平台 -> 终端]"
                        });
                    });

                    // 通用应答
                    _ = SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                }
                // 拦截文本信息下发 (0x8300)
                else if (package.Header.MsgId == 0x8300 && package.Bodies is JT808_0x8300 textDown)
                {
                    // 保存原始字节用于后续重编码切换
                    byte[] rawBytes;
                    try
                    {
                        rawBytes = TextDownlinkEncodingIndex == 1
                            ? System.Text.Encoding.GetEncoding("GBK").GetBytes(textDown.TextInfo)
                            : System.Text.Encoding.UTF8.GetBytes(textDown.TextInfo ?? "");
                    }
                    catch
                    {
                        rawBytes = System.Text.Encoding.UTF8.GetBytes(textDown.TextInfo ?? "");
                    }

                    var msg = new TextDownlinkMessage
                    {
                        Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Content = textDown.TextInfo ?? string.Empty,
                        Flag = textDown.TextFlag,
                        RawBytes = rawBytes
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TextDownlinkMessages.Add(msg);
                    });

                    // TTS语音播报
                    if (msg.IsTTS && EnableTTSPlayback)
                    {
                        string voiceName = SelectedTTSVoice;
                        string textToSpeak = textDown.TextInfo ?? string.Empty;
                        Task.Run(() =>
                        {
                            try
                            {
                                using (var synth = new System.Speech.Synthesis.SpeechSynthesizer())
                                {
                                    if (!string.IsNullOrEmpty(voiceName))
                                    {
                                        synth.SelectVoice(voiceName);
                                    }
                                    synth.Speak(textToSpeak);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log("系统", $"TTS语音播报失败: {ex.Message}");
                            }
                        });
                    }

                    // 回复通用应答
                    _ = SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                }
                // 拦截查询终端参数 (0x8104)
                else if (package.Header.MsgId == 0x8104)
                {
                    var replyBody = new JT808_0x0104
                    {
                        MsgNum = package.Header.MsgNum,
                        ParamList = new System.Collections.Generic.List<JT808_0x8103_BodyBase>()
                    };
                    
                    replyBody.ParamList.Add(new JT808_0x8103_0x0081 { ParamValue = ParseProvinceId(ProvinceIdInput, 11) });
                    replyBody.ParamList.Add(new JT808_0x8103_0x0082 { ParamValue = ParseCityId(CityIdInput, 1101) });
                    replyBody.ParamList.Add(new JT808_0x8103_0x0083 { ParamValue = PlateNo ?? "沪A88888" });
                    replyBody.ParamList.Add(new JT808_0x8103_0x0084 { ParamValue = PlateColor });

                    // 0x0075: 音视频参数
                    replyBody.ParamList.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0075 
                    { 
                        RTS_EncodeMode = 0,
                        RTS_Resolution = 5,
                        RTS_KF_Interval = 250,
                        RTS_Target_FPS = 25,
                        RTS_Target_CodeRate = 0,
                        StreamStore_EncodeMode = 0,
                        StreamStore_Resolution = 5,
                        StreamStore_KF_Interval = 250,
                        StreamStore_Target_FPS = 25,
                        StreamStore_Target_CodeRate = 0,
                        OSD = 1,
                        AudioOutputEnabled = 0
                    });

                    // 0x0076: 音视频通道列表设置
                    var avChannels = new System.Collections.Generic.List<JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0076_AVChannelRefTable>();
                    foreach (var c in VideoChannels)
                    {
                        avChannels.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0076_AVChannelRefTable 
                        {
                            PhysicalChannelNo = c.LogicalChannelNo,
                            LogicChannelNo = c.LogicalChannelNo,
                            ChannelType = 0, // 0:音视频
                            IsConnectCloudPlat = 0
                        });
                    }
                    replyBody.ParamList.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0076 
                    {
                        AVChannelTotal = (byte)avChannels.Count,
                        AudioChannelTotal = 0,
                        VudioChannelTotal = (byte)avChannels.Count,
                        AVChannelRefTables = avChannels
                    });

                    // 0x0077: 单独视频通道参数设置
                    var signalChannels = new System.Collections.Generic.List<JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0077_SignalChannel>();
                    foreach (var c in VideoChannels)
                    {
                        signalChannels.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0077_SignalChannel
                        {
                            LogicChannelNo = c.LogicalChannelNo,
                            RTS_EncodeMode = 0,
                            RTS_Resolution = 5,
                            RTS_KF_Interval = 250,
                            RTS_Target_FPS = 25,
                            RTS_Target_CodeRate = 0,
                            StreamStore_EncodeMode = 0,
                            StreamStore_Resolution = 5,
                            StreamStore_KF_Interval = 250,
                            StreamStore_Target_FPS = 25,
                            StreamStore_Target_CodeRate = 0,
                            OSD = 1
                        });
                    }
                    replyBody.ParamList.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0077 
                    {
                        NeedSetChannelTotal = (byte)signalChannels.Count,
                        SignalChannels = signalChannels
                    });
                    
                    var replyPackage = new JT808Package
                    {
                        Header = new JT808Header
                        {
                            MsgId = 0x0104,
                            MsgNum = 0, // Using 0 as default or we can keep track of SN
                            TerminalPhoneNo = TerminalPhoneNo,
                        },
                        Bodies = replyBody
                    };
                    
                    Task.Run(async () =>
                    {
                        try
                        {
                            var version = UseJT808_2019 ? JT808Version.JTT2019 : JT808Version.JTT2013;
                            byte[] replyData = _protocolManager.Serialize(replyPackage, version);
                            await _networkClient.SendAsync(replyData);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Log("发送", $"查询终端参数应答(0x0104)，响应流水号: {package.Header.MsgNum}，参数个数: {replyBody.ParamList.Count}");
                                Log("发送", $"RAW: {replyData.ToHexString()}");
                                try { Log("解析", _protocolManager.Analyze(replyData)); } catch { }
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Log("发送异常", $"0x0104序列化失败: {ex.Message}");
                            });
                        }
                    });
                }
                // 拦截音视频传输请求 (0x9101)
                else if (package.Header.MsgId == 0x9101)
                {
                    try
                    {
                        var body = package.Bodies as JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x9101;
                        if (body != null)
                        {
                            string ip = body.ServerIp;
                            int port = body.TcpPort > 0 ? body.TcpPort : body.UdpPort;
                            byte channel = body.ChannelNo;
                            
                            var videoItem = VideoChannels.FirstOrDefault(c => c.LogicalChannelNo == channel);
                            if (videoItem != null)
                            {
                                videoItem.StartPushing(ip, port, TerminalPhoneNo);
                            }
                        }

                        _ = SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                    }
                    catch (Exception ex)
                    {
                        Log("异常", $"解析 0x9101 失败: {ex.Message}");
                    }
                }
                // 拦截音视频传输控制 (0x9102)
                else if (package.Header.MsgId == 0x9102)
                {
                    try
                    {
                        var body = package.Bodies as JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x9102;
                        if (body != null)
                        {
                            byte channel = body.ChannelNo;
                            int ctrlCmd = body.ControlCmd;
                            
                            var videoItem = VideoChannels.FirstOrDefault(c => c.LogicalChannelNo == channel);
                            if (videoItem != null)
                            {
                                if (ctrlCmd == 0) // 0表示关闭音视频传输
                                {
                                    videoItem.StopPushing();
                                }
                            }
                        }

                        _ = SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                    }
                    catch (Exception ex)
                    {
                        Log("异常", $"解析 0x9102 失败: {ex.Message}");
                    }
                }
                // 拦截其他需要通用应答的下行指令
                else if (package.Header.MsgId != 0x8100 && package.Header.MsgId != 0x8001 && package.Header.MsgId.ToString("X4").StartsWith("8"))
                {
                    // 大部分8开头的消息（除了8100注册应答、8001平台通用应答等特定消息）默认回复终端通用应答
                    // 在这里做一个保守的默认回复机制
                    _ = SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                }
            }
            catch (Exception ex)
            {
                Log("解析异常", ex.Message);
            }
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsConnected) return;

            ConsoleLogger.LogAction("连接服务器", $"IP={ServerIp}, 端口={ServerPort}");
            try
            {
                Log("系统", $"正在连接 {ServerIp}:{ServerPort}...");
                await _networkClient.ConnectAsync(ServerIp, ServerPort);
                IsConnected = true;
                TerminalStatusText = "已连接 (未注册)";
                TerminalStatusColor = "#FF9800"; // Orange
                Log("系统", "连接成功！");
                StartHeartbeatLoop();
            }
            catch (Exception ex)
            {
                Log("系统", $"连接失败：{ex.Message}");
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            ConsoleLogger.LogAction("断开连接", "正在断开与服务器的连接");
            _networkClient.Disconnect();
            IsConnected = false;
            TerminalStatusText = "未连接";
            TerminalStatusColor = "Gray";
            _heartbeatCts?.Cancel();
        }

        private byte[] AppendRawBytesToJT808Package(byte[] data, byte[] rawAttach)
        {
            if (rawAttach == null || rawAttach.Length == 0) return data;

            // 1. Remove 7E markers
            if (data[0] != 0x7E || data[data.Length - 1] != 0x7E) return data;
            var escaped = new byte[data.Length - 2];
            Array.Copy(data, 1, escaped, 0, data.Length - 2);

            // 2. Unescape
            var unescapedList = new System.Collections.Generic.List<byte>();
            for (int i = 0; i < escaped.Length; i++)
            {
                if (escaped[i] == 0x7D && i + 1 < escaped.Length)
                {
                    if (escaped[i + 1] == 0x01) { unescapedList.Add(0x7D); i++; }
                    else if (escaped[i + 1] == 0x02) { unescapedList.Add(0x7E); i++; }
                    else unescapedList.Add(escaped[i]);
                }
                else
                {
                    unescapedList.Add(escaped[i]);
                }
            }
            var unescaped = unescapedList.ToArray();

            // 3. Extract parts
            // Header (unknown length due to variable phone length in 2019, but we know body length)
            ushort msgProps = (ushort)((unescaped[2] << 8) | unescaped[3]);
            int oldBodyLength = msgProps & 0x03FF;
            int newBodyLength = oldBodyLength + rawAttach.Length;
            
            // Update Body Length in properties
            msgProps = (ushort)((msgProps & ~0x03FF) | (newBodyLength & 0x03FF));
            unescaped[2] = (byte)(msgProps >> 8);
            unescaped[3] = (byte)(msgProps & 0xFF);

            // Insert raw bytes before checksum
            var newUnescaped = new byte[unescaped.Length + rawAttach.Length];
            // Copy everything except the old checksum
            Array.Copy(unescaped, 0, newUnescaped, 0, unescaped.Length - 1);
            // Insert rawAttach
            Array.Copy(rawAttach, 0, newUnescaped, unescaped.Length - 1, rawAttach.Length);

            // 4. Recalculate checksum
            byte checksum = 0;
            for (int i = 0; i < newUnescaped.Length - 1; i++)
            {
                checksum ^= newUnescaped[i];
            }
            newUnescaped[newUnescaped.Length - 1] = checksum;

            // 5. Escape
            var newEscapedList = new System.Collections.Generic.List<byte>();
            newEscapedList.Add(0x7E); // Start
            for (int i = 0; i < newUnescaped.Length; i++)
            {
                if (newUnescaped[i] == 0x7E)
                {
                    newEscapedList.Add(0x7D);
                    newEscapedList.Add(0x02);
                }
                else if (newUnescaped[i] == 0x7D)
                {
                    newEscapedList.Add(0x7D);
                    newEscapedList.Add(0x01);
                }
                else
                {
                    newEscapedList.Add(newUnescaped[i]);
                }
            }
            newEscapedList.Add(0x7E); // End

            return newEscapedList.ToArray();
        }

        private async Task SendPackageAsync<T>(JT808Package package, byte[]? rawAppendBytes = null) where T : JT808Bodies
        {
            if (!IsConnected)
            {
                Log("系统", "请先连接服务器！");
                return;
            }

            try
            {
                JT808Version version = UseJT808_2019 ? JT808Version.JTT2019 : JT808Version.JTT2013;
                byte[] data = _protocolManager.Serialize<T>(package, version);
                
                if (rawAppendBytes != null && rawAppendBytes.Length > 0)
                {
                    data = AppendRawBytesToJT808Package(data, rawAppendBytes);
                }
                
                Log("发送", $"RAW: {data.ToHexString()}");
                string analysis = _protocolManager.Analyze(data);
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var jNode = JsonNode.Parse(analysis);
                    if (jNode != null)
                    {
                        RemoveErrorProperties(jNode);
                        analysis = jNode.ToJsonString(options);
                    }
                }
                catch { }

                Log("发送解析", $"\n{analysis}");

                await _networkClient.SendAsync(data);
            }
            catch (Exception ex)
            {
                Log("系统", $"发送异常: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            ConsoleLogger.LogAction("终端注册", $"手机号={TerminalPhoneNo}, 省域={ProvinceIdInput}, 市县={CityIdInput}, 制造商ID={ManufacturerId}, 终端型号={TerminalModel}, 终端ID={TerminalId}, 车牌号={PlateNo}, 车牌颜色={PlateColor}");
            var header = new JT808Header
            {
                MsgId = 0x0100,
                TerminalPhoneNo = TerminalPhoneNo,
                MsgNum = 1,
            };

            var body = new JT808_0x0100
            {
                AreaID = ParseProvinceId(ProvinceIdInput, 0),
                CityOrCountyId = ParseCityId(CityIdInput, 0),
                MakerId = ManufacturerId,
                TerminalId = TerminalId,
                TerminalModel = TerminalModel,
                PlateColor = PlateColor,
                PlateNo = PlateNo
            };

            var package = new JT808Package
            {
                Header = header,
                Bodies = body
            };

            await SendPackageAsync<JT808_0x0100>(package);
        }

        [RelayCommand]
        private async Task AuthAsync()
        {
            ConsoleLogger.LogAction("终端鉴权", $"手机号={TerminalPhoneNo}, 鉴权码={AuthCode}");
            if (string.IsNullOrEmpty(AuthCode))
            {
                Log("系统", "鉴权码不能为空！请先注册获取或手动输入。");
                return;
            }

            var header = new JT808Header
            {
                MsgId = 0x0102,
                TerminalPhoneNo = TerminalPhoneNo,
                MsgNum = 2,
            };

            var body = new JT808_0x0102
            {
                Code = AuthCode,
                IMEI = "123456789012345",
                SoftwareVersion = "V1.0.0"
            };

            var package = new JT808Package
            {
                Header = header,
                Bodies = body
            };

            await SendPackageAsync<JT808_0x0102>(package);
        }

        [RelayCommand]
        private async Task ReportLocationAsync()
        {
            var snapshot = CaptureLocationReportSnapshot();
            ConsoleLogger.LogAction("发送位置汇报", $"手机号={snapshot.TerminalPhoneNo}, 经度={snapshot.Longitude}, 纬度={snapshot.Latitude}, 速度={snapshot.Speed}, 方向={snapshot.Direction}, 高程={snapshot.Altitude}, 报警标志=0x{snapshot.AlarmFlag:X8}, 状态标志=0x{snapshot.StatusFlag:X8}");

            var header = new JT808Header
            {
                MsgId = 0x0200,
                TerminalPhoneNo = snapshot.TerminalPhoneNo,
                MsgNum = 3,
            };

            var body = new JT808_0x0200
            {
                AlarmFlag = snapshot.AlarmFlag,
                StatusFlag = snapshot.StatusFlag,
                Lat = (int)(snapshot.Latitude * 1000000),
                Lng = (int)(snapshot.Longitude * 1000000),
                Altitude = (ushort)snapshot.Altitude,
                Speed = (ushort)(snapshot.Speed * 10),
                Direction = (ushort)snapshot.Direction,
                GPSTime = DateTime.Now,
                UnknownLocationAttachData = new Dictionary<ushort, byte[]>()
            };

            // 标准附加信息注入 (直接转为RAW字节追加，避免依赖底层库的序列化兼容性问题)
            var standardAttachBytes = new System.Collections.Generic.List<byte>();
            if (snapshot.Enable0x01)
            {
                standardAttachBytes.Add(0x01); standardAttachBytes.Add(0x04);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)snapshot.Mileage0x01)));
            }
            if (snapshot.Enable0x02)
            {
                standardAttachBytes.Add(0x02); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.Oil0x02)));
            }
            if (snapshot.Enable0x03)
            {
                standardAttachBytes.Add(0x03); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.Speed0x03)));
            }
            if (snapshot.Enable0x04)
            {
                standardAttachBytes.Add(0x04); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.AlarmEventId0x04)));
            }
            if (snapshot.Enable0x25)
            {
                standardAttachBytes.Add(0x25); standardAttachBytes.Add(0x04);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)snapshot.ExtVehicleSignal0x25)));
            }
            if (snapshot.Enable0x2A)
            {
                standardAttachBytes.Add(0x2A); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.IOStatus0x2A)));
            }
            if (snapshot.Enable0x2B)
            {
                standardAttachBytes.Add(0x2B); standardAttachBytes.Add(0x04);
                int analog = (snapshot.AnalogAD1 << 16) | snapshot.AnalogAD0;
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(analog)));
            }
            if (snapshot.Enable0x30)
            {
                standardAttachBytes.Add(0x30); standardAttachBytes.Add(0x01);
                standardAttachBytes.Add(snapshot.NetworkSignal0x30);
            }
            if (snapshot.Enable0x31)
            {
                standardAttachBytes.Add(0x31); standardAttachBytes.Add(0x01);
                standardAttachBytes.Add(snapshot.GNSSCount0x31);
            }

            // 自定义 Hex 透传 (格式：ID|Length|Data 或 ID|Data，支持逗号分隔多个)
            var rawAppendBytesList = new System.Collections.Generic.List<byte>();
            Log("系统", $"开始处理位置汇报，当前配置附加项数量: {snapshot.CustomAttachItems.Count}");
            foreach (var attach in snapshot.CustomAttachItems)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(attach.AttachId))
                    {
                        string idStr = attach.AttachId.Replace(" ", "").Replace("-", "").Replace("|", "");
                        if (!string.IsNullOrEmpty(idStr)) rawAppendBytesList.AddRange(idStr.ToHexBytes());
                    }
                    
                    if (!string.IsNullOrWhiteSpace(attach.AttachLength))
                    {
                        string lenStr = attach.AttachLength.Replace(" ", "").Replace("-", "").Replace("|", "");
                        if (!string.IsNullOrEmpty(lenStr)) rawAppendBytesList.AddRange(lenStr.ToHexBytes());
                    }
                    
                    if (!string.IsNullOrWhiteSpace(attach.AttachData))
                    {
                        string dataStr = attach.AttachData.Replace(" ", "").Replace("-", "").Replace("|", "");
                        if (!string.IsNullOrEmpty(dataStr)) rawAppendBytesList.AddRange(dataStr.ToHexBytes());
                    }
                }
                catch (Exception ex)
                {
                    Log("系统", $"自定义附加字段[{attach.AttachId}]解析失败: {ex.Message}");
                }
            }
            if (standardAttachBytes.Count > 0)
            {
                rawAppendBytesList.InsertRange(0, standardAttachBytes);
            }

            Log("系统", $"最终拼接的附加数据(RAW Hex): {(rawAppendBytesList.Count > 0 ? rawAppendBytesList.ToArray().ToHexString() : "无")}");

            var package = new JT808Package
            {
                Header = header,
                Bodies = body
            };

            await SendPackageAsync<JT808_0x0200>(package, rawAppendBytesList.Count > 0 ? rawAppendBytesList.ToArray() : null);
        }

        private async Task SendTerminalGeneralResponseAsync(ushort replyMsgId, ushort replyMsgNum, JT808.Protocol.Enums.JT808TerminalResult result)
        {
            try
            {
                var header = new JT808Header
                {
                    MsgId = 0x0001,
                    TerminalPhoneNo = TerminalPhoneNo,
                    MsgNum = 0 // Will be set in SendPackageAsync if needed, or explicitly increment if managing sequence here
                };

                var body = new JT808_0x0001
                {
                    ReplyMsgId = replyMsgId,
                    ReplyMsgNum = replyMsgNum,
                    TerminalResult = result
                };

                var package = new JT808Package
                {
                    Header = header,
                    Bodies = body
                };

                Log("系统", $"发送终端通用应答(0x0001)，响应消息ID: 0x{replyMsgId:X4}，流水号: {replyMsgNum}");
                await SendPackageAsync<JT808_0x0001>(package);
            }
            catch (Exception ex)
            {
                Log("系统", $"发送通用应答失败: {ex.Message}");
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

                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            await ReportLocationAsync();
                            await Task.Delay(TimeSpan.FromSeconds(AutoReportInterval), token);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                    }
                    finally
                    {
                        Application.Current.Dispatcher.Invoke(() => IsAutoReporting = false);
                    }
                }, token);
            }
        }
        private void StartHeartbeatLoop()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = new System.Threading.CancellationTokenSource();
            var token = _heartbeatCts.Token;

            _ = Task.Run(async () =>
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
                catch { }
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
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    try
                    {
                        _serialPort.Write(ptData, 0, ptData.Length);
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
                var ports = System.IO.Ports.SerialPort.GetPortNames();
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
                _serialPort = new System.IO.Ports.SerialPort(SelectedSerialPort, SelectedBaudRate);
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

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
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                    _serialPort = null;
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

        private void SerialPort_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead <= 0) return;

                byte[] buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);

                _ = HandleSerialDataReceivedAsync(buffer);
            }
            catch (Exception ex)
            {
                Log("系统", $"串口数据读取失败: {ex.Message}");
            }
        }

        private async Task HandleSerialDataReceivedAsync(byte[] data)
        {
            try
            {
                byte ptType = 0;
                try
                {
                    ptType = Convert.ToByte(PassthroughTypeHex, 16);
                }
                catch { }

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

            _ = Task.Run(async () =>
            {
                try
                {
                    DateTime lastReportTime = DateTime.MinValue;
                    double totalDistance = 0;
                    var segmentDistances = new List<double>();
                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        var dist = CalculateDistance(path[i].Lat, path[i].Lng, path[i + 1].Lat, path[i + 1].Lng);
                        segmentDistances.Add(dist);
                        totalDistance += dist;
                    }

                    double currentTraveled = 0;
                    while (currentTraveled < totalDistance && !token.IsCancellationRequested)
                    {
                        // 查找当前所在的线段
                        double distAccum = 0;
                        int segIndex = 0;
                        for (int i = 0; i < segmentDistances.Count; i++)
                        {
                            if (currentTraveled <= distAccum + segmentDistances[i])
                            {
                                segIndex = i;
                                break;
                            }
                            distAccum += segmentDistances[i];
                        }

                        double segmentFraction = 0;
                        if (segmentDistances[segIndex] > 0)
                        {
                            segmentFraction = (currentTraveled - distAccum) / segmentDistances[segIndex];
                        }
                        
                        var p1 = path[segIndex];
                        var p2 = path[segIndex + 1];
                        var currentPt = Interpolate(p1, p2, segmentFraction);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Latitude = Math.Round(currentPt.Lat, 6);
                            Longitude = Math.Round(currentPt.Lng, 6);
                            Direction = CalculateBearing(p1.Lat, p1.Lng, p2.Lat, p2.Lng);
                            OnMapCarMoved?.Invoke(currentPt.Lat, currentPt.Lng); // 传递高精度原始坐标给地图避免偏差
                        });

                        await Task.Delay(100, token); // 100ms
                        
                        double speedMs = Speed * 1000.0 / 3600.0;
                        currentTraveled += speedMs * 0.1; // 0.1s
                    }

                    if (!token.IsCancellationRequested)
                    {
                        Log("系统", "路径模拟行驶已到达终点");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnSimulationFinished?.Invoke();
                        });
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Log("系统", $"路径模拟出错: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnSimulationFinished?.Invoke();
                    });
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => IsPathSimulating = false);
                }
            }, token);
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371e3;
            var phi1 = lat1 * Math.PI / 180;
            var phi2 = lat2 * Math.PI / 180;
            var dPhi = (lat2 - lat1) * Math.PI / 180;
            var dLam = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2) +
                    Math.Cos(phi1) * Math.Cos(phi2) *
                    Math.Sin(dLam / 2) * Math.Sin(dLam / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private int CalculateBearing(double lat1, double lon1, double lat2, double lon2)
        {
            var phi1 = lat1 * Math.PI / 180;
            var phi2 = lat2 * Math.PI / 180;
            var lam1 = lon1 * Math.PI / 180;
            var lam2 = lon2 * Math.PI / 180;

            var y = Math.Sin(lam2 - lam1) * Math.Cos(phi2);
            var x = Math.Cos(phi1) * Math.Sin(phi2) -
                    Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(lam2 - lam1);
            var theta = Math.Atan2(y, x);
            var brng = (theta * 180 / Math.PI + 360) % 360;
            return (int)Math.Round(brng);
        }

        private GeoPoint Interpolate(GeoPoint p1, GeoPoint p2, double fraction)
        {
            return new GeoPoint
            {
                Lat = p1.Lat + (p2.Lat - p1.Lat) * fraction,
                Lng = p1.Lng + (p2.Lng - p1.Lng) * fraction
            };
        }

        [RelayCommand]
        private void ExportConfig()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON 配置文件|*.json",
                FileName = "terminal_config.json"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var config = new WorkStateConfig
                    {
                        ServerIp = ServerIp,
                        ServerPort = ServerPort,
                        TerminalPhoneNo = TerminalPhoneNo,
                        AuthCode = AuthCode,
                        UseJT808_2019 = UseJT808_2019,
                        Speed = Speed,
                        Direction = Direction,
                        Altitude = Altitude,
                        AutoReportInterval = AutoReportInterval,
                        AlarmFlagValue = GetValueFromFlags(AlarmFlags),
                        StatusFlagValue = GetValueFromFlags(StatusFlags),
                        CustomAttachItems = CustomAttachItems.ToList()
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    Log("系统", $"配置已成功导出至: {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    Log("系统", $"配置导出失败: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void ImportConfig()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON 配置文件|*.json"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var config = System.Text.Json.JsonSerializer.Deserialize<WorkStateConfig>(json);
                    if (config != null)
                    {
                        _isLoadingConfig = true;
                        try
                        {
                            ServerIp = config.ServerIp;
                            ServerPort = config.ServerPort;
                            TerminalPhoneNo = config.TerminalPhoneNo;
                            AuthCode = config.AuthCode;
                            UseJT808_2019 = config.UseJT808_2019;
                            Speed = config.Speed;
                            Direction = config.Direction;
                            Altitude = config.Altitude;
                            AutoReportInterval = config.AutoReportInterval;
                            
                            SetFlagsFromValue(AlarmFlags, config.AlarmFlagValue);
                            SetFlagsFromValue(StatusFlags, config.StatusFlagValue);

                            if (config.CustomAttachItems != null)
                            {
                                CustomAttachItems.Clear();
                                foreach (var item in config.CustomAttachItems)
                                {
                                    item.PropertyChanged += (s, e) => SaveConfigDebounced();
                                    CustomAttachItems.Add(item);
                                }
                            }
                        }
                        finally
                        {
                            _isLoadingConfig = false;
                        }
                        
                        SaveConfig(); // Update local config.json immediately
                        Log("系统", $"配置已成功从 {dialog.FileName} 导入");
                    }
                }
                catch (Exception ex)
                {
                    Log("系统", $"配置导入失败: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _autoReportCts?.Cancel();
            _autoReportCts?.Dispose();
            _networkClient?.Dispose();
            CloseSerialPort();

            _simulationTimer?.Dispose();
            _simulationTimer = null;

            // Force save any pending config change immediately on dispose
            if (_saveTimer != null)
            {
                _saveTimer.Dispose();
                _saveTimer = null;
                SaveConfig();
            }
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
