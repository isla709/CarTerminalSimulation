using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class MainViewModel
    {
        private bool _isLoadingConfig = false;
        private readonly object _configLock = new object();
        private System.Threading.Timer? _saveTimer;
        private readonly object _saveLock = new object();
        private Task _lastConfigSaveTask = Task.CompletedTask;

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
            try
            {
                var snapshot = CaptureAppConfig();
                lock (_saveLock)
                {
                    _lastConfigSaveTask = SaveConfigSnapshotAsync(snapshot);
                }
            }
            catch (Exception ex) { Log("配置", $"生成保存快照失败: {ex.Message}"); }
        }

        private void LoadConfig()
        {
            lock (_configLock)
            {
                _isLoadingConfig = true;
                try
                {
                    var configFileExisted = File.Exists(ConfigFile) || File.Exists(ConfigFile + ".tmp");
                    var config = _configStore.Load();
                    {
                            ServerIp = config.ServerIp;
                            ServerPort = config.ServerPort;
                            ServerAddressInput = $"{ServerIp}:{ServerPort}";
                            
                            if (config.ServerAddressHistory != null)
                            {
                                ServerAddressHistory.Clear();
                                foreach (var addr in config.ServerAddressHistory)
                                {
                                    ServerAddressHistory.Add(addr);
                                }
                            }
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
                            ConfigWindowWidth = Math.Clamp(
                                double.IsFinite(config.WindowWidth) ? config.WindowWidth : 1200,
                                900,
                                7680);
                            ConfigWindowHeight = Math.Clamp(
                                double.IsFinite(config.WindowHeight) ? config.WindowHeight : 800,
                                600,
                                4320);

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
                            IncludePrereleaseUpdates = config.IncludePrereleaseUpdates;

                            var bgPath = config.BackgroundImagePath;
                            var bgEffect = config.BackgroundEffectMode;

                            if (!string.IsNullOrEmpty(bgPath))
                            {
                                if (bgPath.StartsWith("pack://embedded/") ||
                                    bgPath.StartsWith("pack://application:,,,/", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Built-in resource
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
                    if (!configFileExisted)
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
                ServerAddressHistory = ServerAddressHistory.ToList(),
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
                ,IncludePrereleaseUpdates = IncludePrereleaseUpdates
            };
        }

        private void SaveConfig()
        {
            AppConfig config;
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                config = dispatcher.Invoke(CaptureAppConfig);
            }
            else
            {
                config = CaptureAppConfig();
            }
            lock (_saveLock)
            {
                _lastConfigSaveTask = SaveConfigSnapshotAsync(config);
            }
        }

        private async Task SaveConfigSnapshotAsync(AppConfig config)
        {
            try { await _configStore.SaveAsync(config).ConfigureAwait(false); }
            catch (Exception ex) { Log("配置", $"保存配置失败: {ex.Message}"); }
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

    }
}
