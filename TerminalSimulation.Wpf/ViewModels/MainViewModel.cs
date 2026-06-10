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
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(value);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    BackgroundImageSource = bmp;
                }
                catch { BackgroundImageSource = null; }
            }
        }


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

            LoadConfig();
            InitThemeImages();

            // 监听属性变化并保存配置
            this.PropertyChanged += (s, e) =>
            {
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
                    e.PropertyName == nameof(BackgroundOpacity))
                {
                    SaveConfig();
                }
            };
        }

        private readonly string ConfigFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        private void InitializeFlags()
        {
            string[] alarmNames = { "紧急报警", "超速报警", "疲劳驾驶", "危险预警", "GNSS模块发生故障", "GNSS天线未接或被剪断", "GNSS天线短路", "终端主电源欠压", "终端主电源掉电", "终端LCD或显示器故障", "TTS模块故障", "摄像头故障", "道路运输证IC卡模块故障", "超速预警", "疲劳驾驶预警", "违规行驶报警", "胎压预警", "右转盲区异常报警", "当天累计驾驶超时", "超时停车", "进出区域", "进出路线", "路段行驶时间不足/过长", "路线偏离报警", "车辆VSS故障", "车辆油量异常", "车辆被盗", "车辆非法点火", "车辆非法位移", "碰撞预警", "侧翻预警", "保留" };
            for (int i = 0; i < alarmNames.Length; i++)
            {
                var item = new BitFlagItem { BitIndex = i, Name = $"[bit{i}]{alarmNames[i]}", IsChecked = false };
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BitFlagItem.IsChecked)) SaveConfig(); };
                AlarmFlags.Add(item);
            }

            string[] statusNames = { "ACC开", "已定位", "南纬", "西经", "停运状态", "经纬度已加密", "保留bit6", "保留bit7", "半载(bit8)", "满载(bit9)", "油路断开", "电路断开", "车门加锁", "前门开", "中门开", "后门开", "驾驶席门开", "自定义门开", "使用GPS卫星", "使用北斗卫星", "使用GLONASS卫星", "使用Galileo卫星", "车辆行驶" };
            for (int i = 0; i < statusNames.Length; i++)
            {
                var item = new BitFlagItem { BitIndex = i, Name = $"[bit{i}]{statusNames[i]}", IsChecked = false };
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BitFlagItem.IsChecked)) SaveConfig(); };
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

        private void LoadConfig()
        {
            try
            {
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
                        BackgroundImagePath = config.BackgroundImagePath;
                        BackgroundEffectMode = config.BackgroundEffectMode;
                        BackgroundOpacity = config.BackgroundOpacity;
                        
                        SetFlagsFromValue(AlarmFlags, config.AlarmFlagValue);
                        SetFlagsFromValue(StatusFlags, config.StatusFlagValue);

                        if (config.CustomAttachItems != null)
                        {
                            CustomAttachItems.Clear();
                            foreach (var item in config.CustomAttachItems)
                            {
                                item.PropertyChanged += (s, e) => SaveConfig();
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
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var config = new AppConfig
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
                    CustomAttachItems = CustomAttachItems.ToList(),
                    BackgroundImagePath = BackgroundImagePath,
                    BackgroundEffectMode = BackgroundEffectMode,
                    BackgroundOpacity = BackgroundOpacity
                };
                var json = System.Text.Json.JsonSerializer.Serialize(config);
                File.WriteAllText(ConfigFile, json);
            }
            catch { }
        }

        [ObservableProperty] private string _serverIp = "127.0.0.1";
        [ObservableProperty] private int _serverPort = 808;
        [ObservableProperty] private string _terminalPhoneNo = "13812345678";
        [ObservableProperty] private string _authCode = "123456";
        [ObservableProperty] private bool _isConnected = false;
        
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
        [ObservableProperty] private string _logText = "";
        private System.Text.StringBuilder _logBuilder = new System.Text.StringBuilder();

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

        [ObservableProperty] private string _terminalStatusText = "未连接";
        [ObservableProperty] private string _terminalStatusColor = "Gray";

        [ObservableProperty] private int _heartbeatInterval = 30;
        [ObservableProperty] private bool _isHeartbeatDisabled = false;
        private System.Threading.CancellationTokenSource? _heartbeatCts;


        public ObservableCollection<ThemeImageItem> ThemeImages { get; } = new ObservableCollection<ThemeImageItem>();

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
                        var item = new ThemeImageItem
                        {
                            FileName = System.IO.Path.GetFileName(file),
                            ImagePath = file,
                            IsSelected = BackgroundImagePath == file,
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
                    SelectThemeImage(ThemeImages.FirstOrDefault(x => x.ImagePath == destFile));
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
                    if (System.IO.File.Exists(item.ImagePath))
                    {
                        System.IO.File.Delete(item.ImagePath);
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
            newItem.PropertyChanged += (s, e) => SaveConfig();
            CustomAttachItems.Add(newItem);
            SaveConfig();
        }

        [RelayCommand]
        private void RemoveCustomAttach(CustomAttachItem item)
        {
            if (item != null)
            {
                CustomAttachItems.Remove(item);
                SaveConfig();
            }
        }

        [RelayCommand]
        private void ClearLogs()
        {
            _logBuilder.Clear();
            LogText = "";
        }

        private void Log(string direction, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] [{direction}] {message}");
                if (_logBuilder.Length > 100000)
                {
                    _logBuilder.Remove(0, _logBuilder.Length - 80000); // Keep last 80K characters
                }
                LogText = _logBuilder.ToString();
            });
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
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        string contentStr;
                        if (PassthroughEncodingIndex == 2) // HEX
                        {
                            contentStr = ptDown.PassthroughData.ToHexString();
                        }
                        else
                        {
                            var encoding = PassthroughEncodingIndex == 0 ? System.Text.Encoding.GetEncoding("GBK") : System.Text.Encoding.UTF8;
                            contentStr = encoding.GetString(ptDown.PassthroughData);
                        }

                        PassthroughMessages.Add(new PassthroughMessage
                        {
                            IsFromServer = true,
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            TypeHex = ptDown.PassthroughType.ToString("X2"),
                            Content = contentStr
                        });
                    });

                    // 通用应答
                    _ = SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
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

        private async Task SendPackageAsync<T>(JT808Package package, byte[] rawAppendBytes = null) where T : JT808Bodies
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
            var header = new JT808Header
            {
                MsgId = 0x0100,
                TerminalPhoneNo = TerminalPhoneNo,
                MsgNum = 1,
            };

            var body = new JT808_0x0100
            {
                AreaID = 0,
                CityOrCountyId = 0,
                MakerId = "test",
                TerminalId = "T001",
                TerminalModel = "Model1",
                PlateColor = 1,
                PlateNo = "京A88888"
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
            var header = new JT808Header
            {
                MsgId = 0x0200,
                TerminalPhoneNo = TerminalPhoneNo,
                MsgNum = 3,
            };

            var body = new JT808_0x0200
            {
                AlarmFlag = GetValueFromFlags(AlarmFlags),
                StatusFlag = GetValueFromFlags(StatusFlags),
                Lat = (int)(Latitude * 1000000),
                Lng = (int)(Longitude * 1000000),
                Altitude = (ushort)Altitude,
                Speed = (ushort)(Speed * 10),
                Direction = (ushort)Direction,
                GPSTime = DateTime.Now,
                UnknownLocationAttachData = new Dictionary<ushort, byte[]>()
            };

            // 标准附加信息：里程
            body.BasicLocationAttachData = new Dictionary<byte, JT808_0x0200_BodyBase>();
            body.BasicLocationAttachData.Add(JT808Constants.JT808_0x0200_0x01, new JT808_0x0200_0x01 { Mileage = 12345 });

            // 自定义 Hex 透传 (格式：ID|Length|Data 或 ID|Data，支持逗号分隔多个)
            var rawAppendBytesList = new System.Collections.Generic.List<byte>();
            Log("系统", $"开始处理位置汇报，当前配置附加项数量: {CustomAttachItems.Count}");
            foreach (var attach in CustomAttachItems)
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

                // Add to UI List
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PassthroughMessages.Add(new PassthroughMessage
                    {
                        IsFromServer = false,
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        TypeHex = ptType.ToString("X2"),
                        Content = PassthroughInputText
                    });
                    PassthroughInputText = "";
                });
            }
            catch (Exception ex)
            {
                Log("系统", $"发送透传失败: {ex.Message}");
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
                    var config = new AppConfig
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
                        
                        SetFlagsFromValue(AlarmFlags, config.AlarmFlagValue);
                        SetFlagsFromValue(StatusFlags, config.StatusFlagValue);

                        if (config.CustomAttachItems != null)
                        {
                            CustomAttachItems.Clear();
                            foreach (var item in config.CustomAttachItems)
                            {
                                item.PropertyChanged += (s, e) => SaveConfig();
                                CustomAttachItems.Add(item);
                            }
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
        }
    }

    public class GeoPoint
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    public class PassthroughMessage
    {
        public bool IsFromServer { get; set; }
        public string Time { get; set; } = "";
        public string TypeHex { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
