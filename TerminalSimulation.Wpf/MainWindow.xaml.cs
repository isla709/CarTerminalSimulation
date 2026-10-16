using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TerminalSimulation.PluginBase;
using TerminalSimulation.Wpf.Helpers;
using TerminalSimulation.Wpf.Services;

namespace TerminalSimulation.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly System.Net.Http.HttpClient MapLocationHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    private static readonly HashSet<string> SupportedMapDefaultLocations = new(StringComparer.Ordinal)
    {
        "auto", "beijing", "shanghai", "guangzhou", "shenzhen", "chengdu", "chongqing", "hangzhou", "wuhan", "xian", "nanjing", "tianjin", "suzhou", "changsha", "zhengzhou", "qingdao", "xiamen", "jinan", "shenyang", "harbin", "kunming", "fuzhou", "hefei", "nanchang", "nanning", "guiyang", "urumqi", "lhasa", "hohhot", "haikou", "lanzhou", "yinchuan", "xining", "taiyuan", "shijiazhuang", "changchun"
    };
    private static readonly HashSet<string> SupportedMapStyles = new(StringComparer.Ordinal)
    {
        "default", "style1"
    };
    private static readonly HashSet<string> SupportedLeafletMapStyles = new(StringComparer.Ordinal)
    {
        "map-style-default", "map-style-dark", "map-style-navy",
        "map-style-emerald", "map-style-grayscale"
    };
    private static readonly HashSet<string> SupportedMapRouteStyles = new(StringComparer.Ordinal)
    {
        "theme-neon-blue", "theme-neon-purple", "theme-solid-red", "theme-solid-orange"
    };
    private const string MapRouteFileMagic = "CTSROUTE/1";
    private static readonly System.Text.Json.JsonSerializerOptions MapRouteJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly System.Windows.Threading.DispatcherTimer _videoLayoutTimer;
    private readonly HashSet<LibVLCSharp.WPF.VideoView> _loadedVideoViews = new();
    private readonly TencentMapService _tencentMapService = new(TencentMapKeyProvider.GetKey);
    private readonly OldMapService _oldMapService = new();
    private CancellationTokenSource? _mapSearchCancellation;
    private CancellationTokenSource? _mapSuggestionCancellation;
    private readonly MapHistoryStore _mapHistoryStore = new(
        System.IO.Path.Combine(PathHelper.ExeDir, "Workspaces", "Map", "new", "history.json"),
        warning => ConsoleLogger.LogInfo($"[Map] {warning}"));
    private readonly MapHistoryStore _oldMapHistoryStore = new(
        System.IO.Path.Combine(PathHelper.ExeDir, "Workspaces", "Map", "old", "history.json"),
        warning => ConsoleLogger.LogInfo($"[Map] {warning}"));
    private readonly AtomicJsonConfigStore<VehicleSimulationOptions> _newVehicleSettings = new(
        System.IO.Path.Combine(PathHelper.ExeDir, "Workspaces", "Map", "new", "vehicle.json"));
    private readonly AtomicJsonConfigStore<VehicleSimulationOptions> _oldVehicleSettings = new(
        System.IO.Path.Combine(PathHelper.ExeDir, "Workspaces", "Map", "old", "vehicle.json"));
    private CancellationTokenSource? _mapRouteCancellation;
    private int _lastMainTabIndex;
    private long _mapTabTransitionVersion;

    public MainWindow()
    {
        InitializeComponent();
        _lastMainTabIndex = MainTabControl.SelectedIndex;
        _videoLayoutTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(32),
            System.Windows.Threading.DispatcherPriority.Render,
            (_, _) => UpdateVideoViewsVisibility(),
            Dispatcher);
        this.Loaded += MainWindow_Loaded;
        this.SizeChanged += MainWindow_SizeChanged;
        UpdateNotificationPopup.CustomPopupPlacementCallback = PlaceUpdateNotification;
        UpdateNotificationPopup.PopupAnimation = SystemParameters.ClientAreaAnimation
            ? System.Windows.Controls.Primitives.PopupAnimation.Fade : System.Windows.Controls.Primitives.PopupAnimation.None;
        LocationChanged += (_, _) => RepositionUpdateNotification();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && DataContext is ViewModels.MainViewModel vm && vm.IsUpdateNotificationOpen)
                vm.DeferUpdateNotification();
        };
        Activated += (_, _) => (DataContext as ViewModels.MainViewModel)?.ShowDeferredUpdateNotification();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ViewModels.MainViewModel { IsUpdateNotificationOpen: true } vm)
            {
                vm.DismissUpdateNotificationCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    internal static System.Windows.Controls.Primitives.CustomPopupPlacement[] PlaceUpdateNotification(Size popup, Size target, Point offset) =>
        [new(new Point(target.Width / 2 - popup.Width + 58 + offset.X, target.Height + 3 + offset.Y),
             System.Windows.Controls.Primitives.PopupPrimaryAxis.Vertical)];

    private void RepositionUpdateNotification()
    {
        if (!UpdateNotificationPopup.IsOpen) return;
        var original = UpdateNotificationPopup.HorizontalOffset;
        UpdateNotificationPopup.HorizontalOffset = original + 1;
        UpdateNotificationPopup.HorizontalOffset = original;
    }

    private void UpdateNotificationPopup_Opened(object? sender, EventArgs e)
    {
        if (!IsActive && DataContext is ViewModels.MainViewModel vm) vm.DeferUpdateNotification();
    }

    private bool _mapInitialized;

    private async Task InitializeMapAsync()
    {
        if (_mapInitialized) return;

        var configuredProvider = (DataContext as TerminalSimulation.Wpf.ViewModels.MainViewModel)?.MapProvider;
        if (string.Equals(configuredProvider, "old", StringComparison.OrdinalIgnoreCase))
        {
            await InitializeLeafletMapAsync();
            return;
        }

        _mapInitialized = true;
        try
        {
            var mapKey = TencentMapKeyProvider.GetKey();
            var mapAppDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "MapApp");
            if (!System.IO.Directory.Exists(mapAppDirectory))
                throw new System.IO.DirectoryNotFoundException($"腾讯地图前端资源不存在：{mapAppDirectory}");

            await MapWebView.EnsureCoreWebView2Async(null);
            MapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "map.car-terminal.local",
                mapAppDirectory,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            await MapWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.__CTS_TENCENT_MAP_KEY={System.Text.Json.JsonSerializer.Serialize(mapKey)};");
            MapWebView.WebMessageReceived -= MapWebView_WebMessageReceived;
            MapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
            MapWebView.CoreWebView2.Navigate("https://map.car-terminal.local/index.html");
            ConsoleLogger.LogInfo("[Map] 已加载新版腾讯地图方案（config.json: MapProvider=new）");
        }
        catch (Exception ex)
        {
            ConsoleLogger.LogError("Map", "腾讯地图方案初始化失败，请检查地图资源、API Key 和网络连接", ex);
            _mapInitialized = false;
        }
    }

    private async Task InitializeLeafletMapAsync()
    {
        if (_mapInitialized) return;
        _mapInitialized = true;
        try
        {
            var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "MapAppOld");
            if (!System.IO.Directory.Exists(directory))
                throw new System.IO.DirectoryNotFoundException($"旧地图前端资源不存在：{directory}");
            await MapWebView.EnsureCoreWebView2Async(null);
            MapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "map-old.car-terminal.local", directory,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            MapWebView.WebMessageReceived -= MapWebView_WebMessageReceived;
            MapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
            MapWebView.CoreWebView2.Navigate("https://map-old.car-terminal.local/index.html");
            ConsoleLogger.LogInfo("[Map] 已加载旧版 Leaflet 地图方案（config.json: MapProvider=old）");
        }
        catch (Exception ex)
        {
            _mapInitialized = false;
            ConsoleLogger.LogError("Map", "Leaflet 地图方案初始化失败，请检查 WebView2 运行时和网络连接", ex);
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep both work areas usable while allowing compact laptop windows.
        ControlColumn.Width = new GridLength(ResponsiveLayoutPolicy.GetMainControlColumnWidth(ActualWidth));
        RepositionUpdateNotification();
        RequestVideoViewsUpdate();
    }

    private async void MapWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(json))
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json);
                if (data != null && DataContext is TerminalSimulation.Wpf.ViewModels.MainViewModel vm)
                {
                    var type = data["type"]?.ToString();
                    if (type == "request_map_settings")
                    {
                        var usesLeaflet = string.Equals(vm.MapProvider, "old", StringComparison.Ordinal);
                        var payload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            defaultLocation = NormalizeMapSetting(vm.MapDefaultLocation, SupportedMapDefaultLocations, "auto"),
                            mapStyle = usesLeaflet
                                ? NormalizeMapSetting(vm.LeafletMapStyle, SupportedLeafletMapStyles, "map-style-default")
                                : NormalizeMapSetting(vm.MapStyle, SupportedMapStyles, "default"),
                            pathStyle = NormalizeMapSetting(vm.MapRouteStyle, SupportedMapRouteStyles, "theme-neon-blue")
                        });
                        await MapWebView.CoreWebView2.ExecuteScriptAsync($"initializeMapSettings({payload})");
                    }
                    else if (type == "save_map_settings")
                    {
                        vm.MapDefaultLocation = NormalizeMapSetting(data["defaultLocation"]?.ToString(), SupportedMapDefaultLocations, "auto");
                        if (string.Equals(vm.MapProvider, "old", StringComparison.Ordinal))
                        {
                            vm.LeafletMapStyle = NormalizeMapSetting(
                                data["mapStyle"]?.ToString(),
                                SupportedLeafletMapStyles,
                                "map-style-default");
                        }
                        else
                        {
                            vm.MapStyle = NormalizeMapSetting(data["mapStyle"]?.ToString(), SupportedMapStyles, "default");
                        }
                        vm.MapRouteStyle = NormalizeMapSetting(data["pathStyle"]?.ToString(), SupportedMapRouteStyles, "theme-neon-blue");
                        vm.LogMessage("地图", "轨迹模拟设置已保存");
                    }
                    else if (type == "save_map_route")
                    {
                        await SaveMapRouteAsync(data["route"]);
                    }
                    else if (type == "open_map_route")
                    {
                        await OpenMapRouteAsync();
                    }
                    else if (type == "map_history")
                    {
                        await HandleMapHistoryAsync(data);
                    }
                    else if (type == "vehicle_settings")
                    {
                        await HandleVehicleSettingsAsync(data, vm.MapProvider);
                    }
                    else if (type == "vehicle_control")
                    {
                        var controls = System.Text.Json.JsonSerializer.Deserialize<VehicleControls>(data["controls"]?.ToJsonString() ?? "{}", MapRouteJsonOptions);
                        if (controls is not null) vm.SetVehicleControls(controls);
                    }
                    else if (type == "request_network_location")
                    {
                        await ResolveMapNetworkLocationAsync();
                    }
                    else if (type == "map_log")
                    {
                        var message = data["message"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(message)) vm.LogMessage("地图", message);
                    }
                    else if (type is "old_search" or "old_suggest" or "old_route")
                    {
                        await HandleOldMapRequestAsync(data, type);
                    }
                    else if (type == "tencent_suggest")
                    {
                        var requestId = data["requestId"]?.ToString() ?? string.Empty;
                        var query = data["query"]?.ToString()?.Trim() ?? string.Empty;
                        if (query.Length is < 2 or > 120 ||
                            !TryReadMapCoordinate(data, "lat", "lng", out var latitude, out var longitude)) return;
                        _mapSuggestionCancellation?.Cancel();
                        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        _mapSuggestionCancellation = cancellation;
                        try
                        {
                            var results = await _tencentMapService.SuggestAsync(query, latitude, longitude, cancellation.Token);
                            if (!ReferenceEquals(_mapSuggestionCancellation, cancellation)) return;
                            await SendTencentMapCallbackAsync("receiveSuggestions", new { requestId, query, succeeded = true, results });
                        }
                        catch (Exception ex)
                        {
                            if (ReferenceEquals(_mapSuggestionCancellation, cancellation))
                                await SendTencentMapCallbackAsync("receiveSuggestions", new
                                {
                                    requestId, query, succeeded = false,
                                    message = ex is OperationCanceledException ? "联想请求超时，可继续手动搜索" : ex.Message,
                                    results = Array.Empty<object>()
                                });
                        }
                        finally
                        {
                            if (ReferenceEquals(_mapSuggestionCancellation, cancellation)) _mapSuggestionCancellation = null;
                            cancellation.Dispose();
                        }
                    }
                    else if (type == "tencent_search")
                    {
                        var requestId = data["requestId"]?.ToString() ?? string.Empty;
                        var query = data["query"]?.ToString() ?? string.Empty;
                        if (query.Trim().Length is < 2 or > 120 || !TryReadMapCoordinate(data, "lat", "lng", out var latitude, out var longitude))
                        {
                            await SendTencentMapCallbackAsync("receiveSearch", new
                            {
                                requestId, succeeded = false, message = "搜索中心坐标无效", results = Array.Empty<object>()
                            });
                            return;
                        }

                        _mapSearchCancellation?.Cancel();
                        _mapSearchCancellation?.Dispose();
                        var searchCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        _mapSearchCancellation = searchCancellation;
                        try
                        {
                            var results = await _tencentMapService.SearchAsync(
                                query, latitude, longitude, searchCancellation.Token);
                            if (!ReferenceEquals(_mapSearchCancellation, searchCancellation)) return;
                            await SendTencentMapCallbackAsync("receiveSearch", new
                            {
                                requestId, succeeded = true, message = string.Empty, results
                            });
                        }
                        catch (OperationCanceledException) when (searchCancellation.IsCancellationRequested)
                        {
                            if (ReferenceEquals(_mapSearchCancellation, searchCancellation))
                            {
                                await SendTencentMapCallbackAsync("receiveSearch", new
                                {
                                    requestId, succeeded = false, message = "腾讯地图搜索超时", results = Array.Empty<object>()
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            ConsoleLogger.LogError("Map", "腾讯地图地点搜索失败", ex);
                            await SendTencentMapCallbackAsync("receiveSearch", new
                            {
                                requestId,
                                succeeded = false,
                                message = ex.Message,
                                errorCode = ex is TencentMapApiException mapError ? mapError.Status : 0,
                                results = Array.Empty<object>()
                            });
                        }
                        finally
                        {
                            if (ReferenceEquals(_mapSearchCancellation, searchCancellation))
                                _mapSearchCancellation = null;
                            searchCancellation.Dispose();
                        }
                    }
                    else if (type == "tencent_route")
                    {
                        var requestId = data["requestId"]?.ToString() ?? string.Empty;
                        if (data["start"] is not System.Text.Json.Nodes.JsonObject start ||
                            data["end"] is not System.Text.Json.Nodes.JsonObject end ||
                            !TryReadMapCoordinate(start, "lat", "lng", out var startLatitude, out var startLongitude) ||
                            !TryReadMapCoordinate(end, "lat", "lng", out var endLatitude, out var endLongitude))
                        {
                            await SendTencentMapCallbackAsync("receiveRoutes", new
                            {
                                requestId, succeeded = false, message = "路线起终点坐标无效", routes = Array.Empty<object>()
                            });
                            return;
                        }

                        _mapRouteCancellation?.Cancel();
                        _mapRouteCancellation?.Dispose();
                        var routeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(18));
                        _mapRouteCancellation = routeCancellation;
                        try
                        {
                            var routes = await _tencentMapService.PlanDrivingAsync(
                                startLatitude,
                                startLongitude,
                                endLatitude,
                                endLongitude,
                                data["startPoiId"]?.ToString(),
                                data["endPoiId"]?.ToString(),
                                routeCancellation.Token);
                            if (!ReferenceEquals(_mapRouteCancellation, routeCancellation)) return;
                            await SendTencentMapCallbackAsync("receiveRoutes", new
                            {
                                requestId, succeeded = true, message = string.Empty, routes
                            });
                        }
                        catch (OperationCanceledException) when (routeCancellation.IsCancellationRequested)
                        {
                            if (ReferenceEquals(_mapRouteCancellation, routeCancellation))
                            {
                                await SendTencentMapCallbackAsync("receiveRoutes", new
                                {
                                    requestId, succeeded = false, message = "腾讯地图路线规划超时", routes = Array.Empty<object>()
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            ConsoleLogger.LogError("Map", "腾讯地图路线规划失败", ex);
                            await SendTencentMapCallbackAsync("receiveRoutes", new
                            {
                                requestId,
                                succeeded = false,
                                message = ex.Message,
                                errorCode = ex is TencentMapApiException mapError ? mapError.Status : 0,
                                routes = Array.Empty<object>()
                            });
                        }
                        finally
                        {
                            if (ReferenceEquals(_mapRouteCancellation, routeCancellation))
                                _mapRouteCancellation = null;
                            routeCancellation.Dispose();
                        }
                    }
                    else if (type == "apply")
                    {
                        var coordinateSystem = ParseMapCoordinateSystem(data["coordinateSystem"]?.ToString());
                        if (data["lat"] != null && double.TryParse(data["lat"]!.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lat))
                        {
                            var longitude = data["lng"] != null && double.TryParse(data["lng"]!.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedLongitude)
                                ? parsedLongitude : vm.Longitude;
                            vm.Latitude = Math.Round(lat, 6);
                            vm.Longitude = Math.Round(longitude, 6);
                            vm.LogMessage("地图", $"已应用 {coordinateSystem.ToProtocolName()} 坐标：{vm.Latitude:F6}, {vm.Longitude:F6}；位置上报保持同一坐标系");
                        }
                    }
                    else if (type == "simulate_path")
                    {
                        VehicleSimulationOptions? vehicle = null;
                        try
                        {
                            if (data["vehicle"] is not null)
                                vehicle = System.Text.Json.JsonSerializer.Deserialize<VehicleSimulationOptions>(data["vehicle"]!.ToJsonString(), MapRouteJsonOptions)?.Validate();
                        }
                        catch (Exception ex)
                        {
                            await SendTencentMapCallbackAsync("simulationAccepted", new { accepted = false, message = ex.Message });
                            return;
                        }
                        var pathArray = data["path"]?.AsArray();
                        if (pathArray != null)
                        {
                            var coordinateSystem = ParseMapCoordinateSystem(data["coordinateSystem"]?.ToString());
                            if (data["speed"] != null && double.TryParse(data["speed"]!.ToString(), out double speed))
                            {
                                vm.Speed = speed;
                            }

                            var geoPoints = new System.Collections.Generic.List<TerminalSimulation.Wpf.ViewModels.GeoPoint>();
                            foreach (var p in pathArray)
                            {
                                if (p != null)
                                {
                                    if (!double.TryParse(p["lat"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mapLat) ||
                                        !double.TryParse(p["lng"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mapLng))
                                    {
                                        continue;
                                    }
                                    geoPoints.Add(new TerminalSimulation.Wpf.ViewModels.GeoPoint
                                    {
                                        Lat = mapLat,
                                        Lng = mapLng
                                    });
                                }
                            }
                            vm.LogMessage("地图", $"路径、模拟及 JT/T 808 上报统一使用 {coordinateSystem.ToProtocolName()} 坐标系");
                            // Attach Action so VM can update car position on map
                            vm.OnMapCarMoved = (lat, lng) =>
                            {
                                MapWebView.CoreWebView2.ExecuteScriptAsync($"updateCarLocationLogical({lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {vm.Direction})");
                            };
                            vm.OnSimulationFinished = () =>
                            {
                                MapWebView.CoreWebView2.ExecuteScriptAsync("simulationFinished()");
                            };
                            vm.OnVehicleTelemetry = telemetry =>
                            {
                                var payload = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    telemetry, coordinateSystem = data["coordinateSystem"]?.ToString() ?? "gcj02"
                                }, MapRouteJsonOptions);
                                MapWebView.CoreWebView2.ExecuteScriptAsync($"window.updateVehicleTelemetry?.({payload})");
                            };
                            try
                            {
                                var accepted = vm.StartPathSimulation(geoPoints, vehicle);
                                await SendTencentMapCallbackAsync("simulationAccepted", new
                                {
                                    accepted, message = accepted ? "" : "无法开始模拟，请检查路线和位置汇报间隔（至少 1 秒）"
                                });
                            }
                            catch (ArgumentException ex)
                            {
                                await SendTencentMapCallbackAsync("simulationAccepted", new { accepted = false, message = ex.Message });
                            }
                        }
                        else await SendTencentMapCallbackAsync("simulationAccepted", new { accepted = false, message = "未提供有效路线" });
                    }
                    else if (type == "simulate_speed")
                    {
                        if (data["speed"] != null && double.TryParse(data["speed"]!.ToString(), out double speed))
                        {
                            vm.Speed = speed;
                        }
                    }
                    else if (type == "stop_simulation")
                    {
                        vm.StopPathSimulation();
                    }
                }
            }
        }
        catch (Exception ex) { ConsoleLogger.LogError("Map", "处理地图消息失败", ex); }
    }

    private async Task SendTencentMapCallbackAsync(string method, object payload)
    {
        if (MapWebView.CoreWebView2 is null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(payload, MapRouteJsonOptions);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"window.ctsMap?.{method}({json})");
    }

    private async Task HandleMapHistoryAsync(System.Text.Json.Nodes.JsonObject data)
    {
        try
        {
            var historyStore = DataContext is ViewModels.MainViewModel { MapProvider: "old" }
                ? _oldMapHistoryStore : _mapHistoryStore;
            var action = data["action"]?.ToString();
            var id = data["id"]?.ToString() ?? "";
            var kind = data["kind"]?.ToString() ?? "";
            switch (action)
            {
                case "search": await historyStore.AddSearchAsync(data["query"]?.ToString() ?? ""); break;
                case "record":
                    var document = ParseAndValidateMapRoute(data["route"]?.ToJsonString(), false);
                    document.ApplicationVersion = AppVersionInfo.FullVersion;
                    var route = System.Text.Json.JsonSerializer.SerializeToNode(document, MapRouteJsonOptions)!.AsObject();
                    await historyStore.AddRouteAsync(data["name"]?.ToString() ?? "轨迹", route, data["automatic"]?.GetValue<bool>() == true);
                    break;
                case "rename": await historyStore.RenameRouteAsync(id, data["name"]?.ToString() ?? ""); break;
                case "delete": await historyStore.DeleteAsync(kind, id); break;
                case "clear": await historyStore.ClearAsync(kind); break;
                case "load":
                    var saved = (await historyStore.ReadAsync()).Routes.FirstOrDefault(item => item.Id == id)
                        ?? throw new System.IO.InvalidDataException("轨迹记录已不存在");
                    var validated = ParseAndValidateMapRoute(saved.Route.ToJsonString(), true);
                    await MapWebView.CoreWebView2.ExecuteScriptAsync(
                        $"loadRouteFile({System.Text.Json.JsonSerializer.Serialize(validated, MapRouteJsonOptions)})");
                    break;
                case "list": break;
                default: throw new System.IO.InvalidDataException("未知记录操作");
            }
            var snapshot = await historyStore.ReadSummaryAsync();
            await SendTencentMapCallbackAsync("receiveHistory", new
            {
                succeeded = true, action, automatic = data["automatic"]?.GetValue<bool>() ?? false,
                searches = snapshot.Searches,
                routes = snapshot.Routes
            });
        }
        catch (Exception ex)
        {
            ConsoleLogger.LogError("Map", "地图记录操作失败", ex);
            await SendTencentMapCallbackAsync("receiveHistory", new { succeeded = false, action = data["action"]?.ToString(), message = ex.Message });
        }
    }

    private async Task HandleVehicleSettingsAsync(System.Text.Json.Nodes.JsonObject data, string provider)
    {
        try
        {
            var store = provider == "old" ? _oldVehicleSettings : _newVehicleSettings;
            var action = data["action"]?.ToString() ?? "get";
            VehicleSimulationOptions options;
            if (action == "save")
            {
                options = System.Text.Json.JsonSerializer.Deserialize<VehicleSimulationOptions>(data["options"]?.ToJsonString() ?? "{}", MapRouteJsonOptions)!.Validate();
                await store.SaveAsync(options);
            }
            else options = store.Load().Validate();
            await SendTencentMapCallbackAsync("receiveVehicleSettings", new
            {
                succeeded = true, action, options, presets = VehicleSimulationOptions.Presets, trafficEventCatalog = TrafficEventCatalog.All
            });
        }
        catch (Exception ex)
        {
            await SendTencentMapCallbackAsync("receiveVehicleSettings", new { succeeded = false, action = data["action"]?.ToString(), message = ex.Message });
        }
    }

    private async Task HandleOldMapRequestAsync(System.Text.Json.Nodes.JsonObject data, string type)
    {
        var requestId = data["requestId"]?.ToString() ?? "";
        var query = data["query"]?.ToString()?.Trim() ?? "";
        var isRoute = type == "old_route";
        var isSuggestion = type == "old_suggest";
        var callback = isRoute ? "receiveRoutes" : isSuggestion ? "receiveSuggestions" : "receiveSearch";
        var previous = isRoute ? _mapRouteCancellation : isSuggestion ? _mapSuggestionCancellation : _mapSearchCancellation;
        previous?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        if (isRoute) _mapRouteCancellation = cancellation;
        else if (isSuggestion) _mapSuggestionCancellation = cancellation;
        else _mapSearchCancellation = cancellation;
        bool IsCurrent() => ReferenceEquals(cancellation, isRoute ? _mapRouteCancellation : isSuggestion ? _mapSuggestionCancellation : _mapSearchCancellation);
        try
        {
            if (isRoute)
            {
                if (data["start"] is not System.Text.Json.Nodes.JsonObject start ||
                    data["end"] is not System.Text.Json.Nodes.JsonObject end ||
                    !TryReadMapCoordinate(start, "lat", "lng", out var startLat, out var startLng) ||
                    !TryReadMapCoordinate(end, "lat", "lng", out var endLat, out var endLng))
                    throw new InvalidOperationException("路线起终点坐标无效");
                // Display tiles are GCJ-02; OSRM/Photon services require WGS-84.
                // This conversion is service-side only, never an implicit change
                // to the user's route-file or JT/T 808 reporting coordinate system.
                var from = GeoCoordinateConverter.Convert(startLat, startLng, GeoCoordinateSystem.Gcj02, GeoCoordinateSystem.Wgs84);
                var to = GeoCoordinateConverter.Convert(endLat, endLng, GeoCoordinateSystem.Gcj02, GeoCoordinateSystem.Wgs84);
                var routes = await _oldMapService.PlanAsync(from.Latitude, from.Longitude, to.Latitude, to.Longitude, cancellation.Token);
                if (IsCurrent()) await SendTencentMapCallbackAsync(callback, new { requestId, succeeded = true, routes });
            }
            else
            {
                if (query.Length is < 2 or > 120 || !TryReadMapCoordinate(data, "lat", "lng", out var lat, out var lng))
                    throw new InvalidOperationException("搜索关键词或中心坐标无效");
                var center = GeoCoordinateConverter.Convert(lat, lng, GeoCoordinateSystem.Gcj02, GeoCoordinateSystem.Wgs84);
                var results = await _oldMapService.SearchAsync(query, center.Latitude, center.Longitude, cancellation.Token);
                if (IsCurrent()) await SendTencentMapCallbackAsync(callback, new { requestId, query, succeeded = true, results });
            }
        }
        catch (Exception ex)
        {
            if (IsCurrent()) await SendTencentMapCallbackAsync(callback, new
            {
                requestId, query, succeeded = false,
                message = ex is OperationCanceledException ? "服务请求超时，请稍后重试" : ex.Message,
                results = Array.Empty<object>(), routes = Array.Empty<object>()
            });
        }
        finally
        {
            if (IsCurrent())
            {
                if (isRoute) _mapRouteCancellation = null;
                else if (isSuggestion) _mapSuggestionCancellation = null;
                else _mapSearchCancellation = null;
            }
        }
    }

    private static bool TryReadMapCoordinate(
        System.Text.Json.Nodes.JsonObject source,
        string latitudeProperty,
        string longitudeProperty,
        out double latitude,
        out double longitude)
    {
        latitude = 0;
        longitude = 0;
        var valid = double.TryParse(
                        source[latitudeProperty]?.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out latitude) &&
                    double.TryParse(
                        source[longitudeProperty]?.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out longitude);
        return valid && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static string NormalizeMapSetting(string? value, HashSet<string> supportedValues, string fallback)
    {
        return value != null && supportedValues.Contains(value) ? value : fallback;
    }

    private async Task SaveMapRouteAsync(System.Text.Json.Nodes.JsonNode? routeNode)
    {
        try
        {
            var document = ParseAndValidateMapRoute(routeNode?.ToJsonString(), requireFileMetadata: false);
            document.FormatVersion = 1;
            document.SavedAtUtc = DateTimeOffset.UtcNow;
            document.ApplicationVersion = AppVersionInfo.FullVersion;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存地图路线",
                Filter = "车载终端模拟路线 (*.ctsroute)|*.ctsroute",
                DefaultExt = ".ctsroute",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"route-{DateTime.Now:yyyyMMdd-HHmmss}.ctsroute"
            };
            if (dialog.ShowDialog(this) != true) return;

            var json = System.Text.Json.JsonSerializer.Serialize(document, MapRouteJsonOptions);
            var content = MapRouteFileMagic + Environment.NewLine + json;
            var temporaryPath = dialog.FileName + $".tmp-{Guid.NewGuid():N}";
            try
            {
                await System.IO.File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                System.IO.File.Move(temporaryPath, dialog.FileName, overwrite: true);
            }
            finally
            {
                if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
            }

            if (DataContext is TerminalSimulation.Wpf.ViewModels.MainViewModel vm)
            {
                vm.LogMessage("地图", $"路线已保存：{dialog.FileName}（{document.CoordinateSystem.ToUpperInvariant()}，{document.Points.Count} 个点）");
            }
            await NotifyMapRouteOperationAsync("save", true, $"路线已保存为 {System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            ConsoleLogger.LogError("Map", "保存路线失败", ex);
            await NotifyMapRouteOperationAsync("save", false, $"保存路线失败：{ex.Message}");
        }
    }

    private async Task OpenMapRouteAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "打开地图路线",
                Filter = "车载终端模拟路线 (*.ctsroute)|*.ctsroute",
                DefaultExt = ".ctsroute",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            var fileInfo = new System.IO.FileInfo(dialog.FileName);
            if (fileInfo.Length > 25 * 1024 * 1024)
            {
                throw new System.IO.InvalidDataException("路线文件超过 25 MB 限制");
            }

            var content = await System.IO.File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
            content = content.TrimStart('\uFEFF');
            var lineBreakIndex = content.IndexOf('\n');
            if (lineBreakIndex < 0 || !string.Equals(content[..lineBreakIndex].TrimEnd('\r'), MapRouteFileMagic, StringComparison.Ordinal))
            {
                throw new System.IO.InvalidDataException("不是有效的 .ctsroute 路线文件");
            }

            var document = ParseAndValidateMapRoute(content[(lineBreakIndex + 1)..], requireFileMetadata: true);
            var payload = System.Text.Json.JsonSerializer.Serialize(document, MapRouteJsonOptions);
            await MapWebView.CoreWebView2.ExecuteScriptAsync($"loadRouteFile({payload})");
            if (DataContext is TerminalSimulation.Wpf.ViewModels.MainViewModel vm)
            {
                vm.LogMessage("地图", $"已打开路线：{dialog.FileName}（{document.CoordinateSystem.ToUpperInvariant()}，{document.Points.Count} 个点）");
            }
        }
        catch (Exception ex)
        {
            ConsoleLogger.LogError("Map", "打开路线失败", ex);
            await NotifyMapRouteOperationAsync("open", false, $"打开路线失败：{ex.Message}");
        }
    }

    private static MapRouteDocument ParseAndValidateMapRoute(string? json, bool requireFileMetadata)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new System.IO.InvalidDataException("路线数据为空");
        MapRouteDocument document;
        try
        {
            document = System.Text.Json.JsonSerializer.Deserialize<MapRouteDocument>(json, MapRouteJsonOptions)
                ?? throw new System.IO.InvalidDataException("路线数据为空");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new System.IO.InvalidDataException("路线文件内容损坏", ex);
        }

        if (requireFileMetadata && document.FormatVersion != 1)
        {
            throw new System.IO.InvalidDataException($"不支持的路线格式版本：{document.FormatVersion}");
        }
        document.Mode = document.Mode?.Trim().ToLowerInvariant() switch
        {
            "draw" => "draw",
            "navigation" => "navigation",
            "single" => "single",
            _ => throw new System.IO.InvalidDataException("路线模式无效")
        };
        document.CoordinateSystem = document.CoordinateSystem?.Trim().ToLowerInvariant() switch
        {
            "wgs84" => "wgs84",
            "gcj02" => "gcj02",
            "bd09" => "bd09",
            _ => throw new System.IO.InvalidDataException("路线坐标系无效")
        };
        if (document.Points is null || document.Points.Count < (document.Mode == "single" ? 1 : 2) ||
            (document.Mode == "single" && document.Points.Count != 1))
        {
            throw new System.IO.InvalidDataException("路线至少需要两个轨迹点；单点模式只允许一个点");
        }
        if (document.Points.Count > 100_000)
        {
            throw new System.IO.InvalidDataException("路线轨迹点超过 100000 个限制");
        }
        foreach (var point in document.Points) ValidateMapRoutePoint(point);
        if (document.Start is not null) ValidateMapRoutePoint(document.Start);
        if (document.End is not null) ValidateMapRoutePoint(document.End);
        if (document.Mode == "navigation")
        {
            document.Start ??= document.Points[0] with { Name = "保存路线起点" };
            document.End ??= document.Points[^1] with { Name = "保存路线终点" };
        }
        document.Speed = double.IsFinite(document.Speed) ? Math.Clamp(document.Speed, 1, 300) : 60.5;
        if (document.Vehicle is not null) document.Vehicle = document.Vehicle.Validate();
        return document;
    }

    private static void ValidateMapRoutePoint(MapRoutePoint point)
    {
        if (!double.IsFinite(point.Lat) || !double.IsFinite(point.Lng) || point.Lat is < -90 or > 90 || point.Lng is < -180 or > 180)
        {
            throw new System.IO.InvalidDataException("路线中包含无效坐标");
        }
        if (point.Name?.Length > 200) point.Name = point.Name[..200];
    }

    private async Task NotifyMapRouteOperationAsync(string operation, bool succeeded, string message)
    {
        var operationJson = System.Text.Json.JsonSerializer.Serialize(operation);
        var messageJson = System.Text.Json.JsonSerializer.Serialize(message);
        await MapWebView.CoreWebView2.ExecuteScriptAsync($"routeFileOperationResult({operationJson},{succeeded.ToString().ToLowerInvariant()},{messageJson})");
    }

    private sealed class MapRouteDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string ApplicationVersion { get; set; } = "";
        public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Mode { get; set; } = "draw";
        public string CoordinateSystem { get; set; } = "gcj02";
        public double Speed { get; set; } = 60.5;
        public VehicleSimulationOptions? Vehicle { get; set; }
        public List<MapRoutePoint> Points { get; set; } = new();
        public MapRoutePoint? Start { get; set; }
        public MapRoutePoint? End { get; set; }
    }

    private sealed record MapRoutePoint
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public string Name { get; set; } = "";
    }

    private async Task ResolveMapNetworkLocationAsync()
    {
        try
        {
            var usesTencent = DataContext is TerminalSimulation.Wpf.ViewModels.MainViewModel vm &&
                              string.Equals(vm.MapProvider, "new", StringComparison.Ordinal);
            if (usesTencent)
            {
                try
                {
                    var tencentLocation = await _tencentMapService.LocateByIpAsync();
                    var wgsLocation = GeoCoordinateConverter.Convert(
                        tencentLocation.Latitude,
                        tencentLocation.Longitude,
                        GeoCoordinateSystem.Gcj02,
                        GeoCoordinateSystem.Wgs84);
                    var tencentPayload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        lat = wgsLocation.Latitude,
                        lng = wgsLocation.Longitude,
                        accuracy = 25000,
                        label = tencentLocation.Label
                    });
                    await MapWebView.CoreWebView2.ExecuteScriptAsync($"applyNetworkLocation({tencentPayload})");
                    return;
                }
                catch (Exception ex)
                {
                    ConsoleLogger.LogInfo($"[Map] 腾讯地图 IP 定位不可用，将尝试备用源：{ex.Message}");
                }
            }

            Exception? lastProviderError = null;
            var providers = new[]
            {
                "http://ip-api.com/json/?lang=zh-CN",
                "https://ipwho.is/"
            };

            foreach (var provider in providers)
            {
                try
                {
                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, provider);
                    request.Headers.Accept.ParseAdd("application/json");
                    request.Headers.UserAgent.ParseAdd($"CarTerminalSimulation/{AppVersionInfo.AppVersion}");
                    using var response = await MapLocationHttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var root = System.Text.Json.Nodes.JsonNode.Parse(responseJson)?.AsObject()
                        ?? throw new InvalidOperationException("网络定位服务返回了空数据");
                    if ((bool.TryParse(root["success"]?.ToString(), out var success) && !success) ||
                        string.Equals(root["status"]?.ToString(), "fail", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(root["message"]?.ToString() ?? "网络定位服务未返回有效位置");
                    }

                    var latitudeText = root["latitude"]?.ToString() ?? root["lat"]?.ToString();
                    var longitudeText = root["longitude"]?.ToString() ?? root["lon"]?.ToString();
                    if (!double.TryParse(latitudeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var latitude) ||
                        !double.TryParse(longitudeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var longitude))
                    {
                        throw new InvalidOperationException("网络定位结果缺少经纬度");
                    }

                    _ = double.TryParse(root["accuracy_radius"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var accuracyRadiusKm);
                    var locationParts = new[]
                    {
                        root["city"]?.ToString(),
                        root["regionName"]?.ToString() ?? root["region"]?.ToString(),
                        root["country"]?.ToString()
                    };
                    var label = string.Join(" · ", locationParts.Where(static value => !string.IsNullOrWhiteSpace(value)));
                    var payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        lat = latitude,
                        lng = longitude,
                        accuracy = Math.Max(5000, accuracyRadiusKm * 1000),
                        label
                    });
                    await MapWebView.CoreWebView2.ExecuteScriptAsync($"applyNetworkLocation({payload})");
                    return;
                }
                catch (Exception ex)
                {
                    lastProviderError = ex;
                    ConsoleLogger.LogInfo($"[Map] 网络定位源不可用 ({new Uri(provider).Host})：{ex.Message}");
                }
            }

            throw new InvalidOperationException("所有网络定位源均不可用", lastProviderError);
        }
        catch (Exception ex)
        {
            ConsoleLogger.LogInfo($"[Map] 设备网络定位不可用：{ex.Message}");
            var message = System.Text.Json.JsonSerializer.Serialize(ex is TaskCanceledException ? "网络定位超时" : ex.Message);
            await MapWebView.CoreWebView2.ExecuteScriptAsync($"networkLocationFailed({message})");
        }
    }

    private static GeoCoordinateSystem ParseMapCoordinateSystem(string? value)
    {
        GeoCoordinateConverter.TryParse(value, out var coordinateSystem);
        return coordinateSystem;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }
        else
        {
            this.DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalSimulation.Wpf.ViewModels.MainViewModel vm)
        {
            vm.DismissUpdateNotificationCommand.Execute(null);
            vm.IsSettingsOpen = true;
        }
    }

    private bool _openUpdateDetailsRequested;
    private void BtnUpdateDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        vm.DismissUpdateNotificationCommand.Execute(null);
        if (_settingsWindow is not null) { _settingsWindow.OpenAboutAndVersions(); return; }
        _openUpdateDetailsRequested = true;
        vm.IsSettingsOpen = true;
    }

    private ThemeSettingsWindow? _settingsWindow;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ConstrainWindowToWorkingArea();
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }
        catch (Exception ex) { ConsoleLogger.LogError("Window", "注册窗口消息钩子失败", ex); }

        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
            _ = vm.CheckForUpdatesSilentlyAsync();
        }

    }

    private void ConstrainWindowToWorkingArea()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, 2);
        var dpi = VisualTreeHelper.GetDpi(this);
        Rect workArea = SystemParameters.WorkArea;
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            workArea = new Rect(
                info.WorkArea.Left / dpi.DpiScaleX,
                info.WorkArea.Top / dpi.DpiScaleY,
                (info.WorkArea.Right - info.WorkArea.Left) / dpi.DpiScaleX,
                (info.WorkArea.Bottom - info.WorkArea.Top) / dpi.DpiScaleY);
        }
        Width = Math.Clamp(Width, MinWidth, Math.Max(MinWidth, workArea.Width));
        Height = Math.Clamp(Height, MinHeight, Math.Max(MinHeight, workArea.Height));
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsUtilityPickerClosing) &&
            sender is ViewModels.MainViewModel { IsUtilityPickerClosing: true })
        {
            AnimateUtilityPickerClose();
            return;
        }

        if (e.PropertyName == nameof(TerminalSimulation.Wpf.ViewModels.MainViewModel.IsSettingsOpen))
        {
            if (DataContext is TerminalSimulation.Wpf.ViewModels.MainViewModel vm)
            {
                if (vm.IsSettingsOpen)
                {
                    if (_settingsWindow == null)
                    {
                        var openVersions = _openUpdateDetailsRequested;
                        _openUpdateDetailsRequested = false;
                        _settingsWindow = new ThemeSettingsWindow(openVersions)
                        {
                            Owner = this,
                            DataContext = vm,
                            Width = this.ActualWidth,
                            Height = this.ActualHeight,
                            Left = this.Left,
                            Top = this.Top
                        };
                        _settingsWindow.Closed += (s, args) =>
                        {
                            _settingsWindow = null;
                            vm.IsSettingsOpen = false;
                        };
                        _settingsWindow.ShowDialog();
                    }
                }
                else
                {
                    if (_settingsWindow != null)
                    {
                        _settingsWindow.Close();
                        _settingsWindow = null;
                    }
                }
            }
        }
    }

    private bool _shutdownInProgress;
    private bool _shutdownCompleted;

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress) return;
        (DataContext as ViewModels.MainViewModel)?.DismissUpdateNotificationCommand.Execute(null);
        _shutdownInProgress = true;
        try
        {
            if (DataContext is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (DataContext is IDisposable disposable) disposable.Dispose();
        }
        catch (Exception ex) { ConsoleLogger.LogError("Shutdown", "等待后台任务退出失败", ex); }
        finally
        {
            _shutdownCompleted = true;
            _shutdownInProgress = false;
            Close();
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _mapSuggestionCancellation?.Cancel();
        _mapSearchCancellation?.Cancel();
        _mapRouteCancellation?.Cancel();
        _videoLayoutTimer.Stop();
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.PropertyChanged -= Vm_PropertyChanged;
        }
        base.OnClosed(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    private static IntPtr GetHwnd(DependencyObject control)
    {
        if (control is System.Windows.Interop.HwndHost hwndHost)
        {
            return hwndHost.Handle;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(control); i++)
        {
            var child = VisualTreeHelper.GetChild(control, i);
            var hwnd = GetHwnd(child);
            if (hwnd != IntPtr.Zero) return hwnd;
        }
        return IntPtr.Zero;
    }

    private void VideoScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RequestVideoViewsUpdate();
    }

    private void VideoScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RequestVideoViewsUpdate();
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == MainTabControl)
        {
            var selectedIndex = MainTabControl.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex != _lastMainTabIndex)
            {
                var direction = selectedIndex > _lastMainTabIndex ? 1d : -1d;
                _lastMainTabIndex = selectedIndex;

                var mapTransitionVersion = ++_mapTabTransitionVersion;
                MapWebView.Visibility = Visibility.Hidden;
                MapTransitionMask.Visibility = Visibility.Visible;

                if (MainTabControl.SelectedItem is TabItem selectedTab &&
                    selectedTab.Content is FrameworkElement content)
                {
                    if (selectedIndex == 1)
                    {
                        TabTransitionAnimator.Animate(
                            content,
                            direction,
                            () => CompleteMapTabTransition(mapTransitionVersion));
                    }
                    else
                    {
                        TabTransitionAnimator.Animate(content, direction);
                    }
                }
            }

            if (MainTabControl.SelectedIndex == 1)
            {
                _ = InitializeMapAsync();
            }
            RequestVideoViewsUpdate();
        }
    }

    private void CompleteMapTabTransition(long transitionVersion)
    {
        if (transitionVersion != _mapTabTransitionVersion ||
            MainTabControl.SelectedIndex != 1)
        {
            return;
        }

        // Perform both visibility changes in the same render cycle so the HWND
        // WebView never appears above a still-running WPF transition.
        MapWebView.Visibility = Visibility.Visible;
        MapTransitionMask.Visibility = Visibility.Collapsed;
    }

    private void UtilityTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != UtilityTabControl)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                var selectedHeader = UtilityTabControl.ItemContainerGenerator.ContainerFromItem(
                    UtilityTabControl.SelectedItem) as FrameworkElement
                    ?? UtilityTabControl.SelectedItem as FrameworkElement;

                selectedHeader?.BringIntoView();
                UpdateUtilityTabScrollButtons(FindUtilityTemplatePart<ScrollViewer>("UtilityTabHeaderScroller"));
            }));
    }

    private void UtilityPickerOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement overlay)
        {
            return;
        }

        if (!overlay.IsVisible)
        {
            ResetUtilityPickerVisuals();
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(() =>
            {
                AnimateUtilityPickerOpen();
                AnimateUtilityPickerItems();
            }));
    }

    private void AnimateUtilityPickerOpen()
    {
        ResetUtilityPickerAnimationClocks();
        UtilityPickerBackdrop.Opacity = 0.46;
        UtilityPickerCard.Opacity = 1;
        UtilityPickerScaleTransform.ScaleX = 1;
        UtilityPickerScaleTransform.ScaleY = 1;
        UtilityPickerTranslateTransform.Y = 0;

        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        UtilityPickerBackdrop.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 0.46, TimeSpan.FromMilliseconds(180))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerCard.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
    }

    private void AnimateUtilityPickerClose()
    {
        if (!UtilityPickerOverlay.IsVisible)
        {
            return;
        }

        var backdropOpacity = UtilityPickerBackdrop.Opacity;
        var cardOpacity = UtilityPickerCard.Opacity;
        var scaleX = UtilityPickerScaleTransform.ScaleX;
        var scaleY = UtilityPickerScaleTransform.ScaleY;
        var translateY = UtilityPickerTranslateTransform.Y;

        ResetUtilityPickerAnimationClocks();
        UtilityPickerBackdrop.Opacity = 0;
        UtilityPickerCard.Opacity = 0;
        UtilityPickerScaleTransform.ScaleX = 0.97;
        UtilityPickerScaleTransform.ScaleY = 0.97;
        UtilityPickerTranslateTransform.Y = 8;

        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        UtilityPickerBackdrop.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(backdropOpacity, 0, TimeSpan.FromMilliseconds(160))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerCard.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(cardOpacity, 0, TimeSpan.FromMilliseconds(160))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scaleX, 0.97, TimeSpan.FromMilliseconds(160))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scaleY, 0.97, TimeSpan.FromMilliseconds(160))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
        UtilityPickerTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(translateY, 8, TimeSpan.FromMilliseconds(160))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = ease
            });
    }

    private void ResetUtilityPickerVisuals()
    {
        ResetUtilityPickerAnimationClocks();
        UtilityPickerBackdrop.Opacity = 0;
        UtilityPickerCard.Opacity = 0;
        UtilityPickerScaleTransform.ScaleX = 0.94;
        UtilityPickerScaleTransform.ScaleY = 0.94;
        UtilityPickerTranslateTransform.Y = 18;
    }

    private void ResetUtilityPickerAnimationClocks()
    {
        UtilityPickerBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
        UtilityPickerCard.BeginAnimation(UIElement.OpacityProperty, null);
        UtilityPickerScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        UtilityPickerScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        UtilityPickerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void AnimateUtilityPickerItems()
    {
        UtilityPluginPickerItems.UpdateLayout();

        for (var index = 0; index < UtilityPluginPickerItems.Items.Count; index++)
        {
            if (UtilityPluginPickerItems.ItemContainerGenerator.ContainerFromIndex(index) is not UIElement container)
            {
                continue;
            }

            container.BeginAnimation(UIElement.OpacityProperty, null);
            container.Opacity = 1;

            if (!SystemParameters.ClientAreaAnimation)
            {
                container.RenderTransform = Transform.Identity;
                continue;
            }

            var translate = new TranslateTransform(0, 0);
            container.RenderTransform = translate;
            container.RenderTransformOrigin = new Point(0.5, 0.5);

            var delay = TimeSpan.FromMilliseconds(70 + (Math.Min(index, 8) * 36));
            var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210))
            {
                BeginTime = delay,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var translateAnimation = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(250))
            {
                BeginTime = delay,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            container.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            translate.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }
    }

    private void UtilityTabHeaderScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateUtilityTabScrollButtons(sender as ScrollViewer);
    }

    private void UtilityTabHeaderScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller || scroller.ScrollableWidth <= 0)
        {
            return;
        }

        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void UtilityTabScrollLeftButton_Click(object sender, RoutedEventArgs e)
    {
        var scroller = FindUtilityTemplatePart<ScrollViewer>("UtilityTabHeaderScroller");
        scroller?.ScrollToHorizontalOffset(Math.Max(0, scroller.HorizontalOffset - 180));
    }

    private void UtilityTabScrollRightButton_Click(object sender, RoutedEventArgs e)
    {
        var scroller = FindUtilityTemplatePart<ScrollViewer>("UtilityTabHeaderScroller");
        scroller?.ScrollToHorizontalOffset(
            Math.Min(scroller.ScrollableWidth, scroller.HorizontalOffset + 180));
    }

    private T? FindUtilityTemplatePart<T>(string name) where T : FrameworkElement
    {
        return UtilityTabControl.Template.FindName(name, UtilityTabControl) as T;
    }

    private void UpdateUtilityTabScrollButtons(ScrollViewer? scroller)
    {
        var leftButton = FindUtilityTemplatePart<Button>("UtilityTabScrollLeftButton");
        var rightButton = FindUtilityTemplatePart<Button>("UtilityTabScrollRightButton");
        var inlineAddButton = FindUtilityTemplatePart<Button>("UtilityInlineAddButton");
        var pinnedAddButton = FindUtilityTemplatePart<Button>("UtilityPinnedAddButton");
        var headerPanel = FindUtilityTemplatePart<FrameworkElement>("HeaderPanel");
        var headerRow = scroller?.Parent as FrameworkElement;
        if (scroller == null || leftButton == null || rightButton == null ||
            inlineAddButton == null || pinnedAddButton == null ||
            headerPanel == null || headerRow == null)
        {
            return;
        }

        // Determine overflow against the whole header row, independent of the space
        // currently occupied by the overflow controls. This avoids a layout feedback
        // loop where visible arrow buttons prevent the header from returning inline.
        const double inlineAddButtonFootprint = 42;
        var tabHeadersWidth = Math.Max(headerPanel.ActualWidth, headerPanel.DesiredSize.Width);
        var hasOverflow = tabHeadersWidth + inlineAddButtonFootprint > headerRow.ActualWidth + 0.5;
        var visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        leftButton.Visibility = visibility;
        rightButton.Visibility = visibility;
        pinnedAddButton.Visibility = visibility;
        inlineAddButton.Visibility = hasOverflow ? Visibility.Collapsed : Visibility.Visible;
        leftButton.IsEnabled = hasOverflow && scroller.HorizontalOffset > 0.5;
        rightButton.IsEnabled = hasOverflow &&
                                scroller.HorizontalOffset < scroller.ScrollableWidth - 0.5;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_EXITSIZEMOVE = 0x0232;

        if (msg == WM_EXITSIZEMOVE)
        {
            RequestVideoViewsUpdate();
        }

        return IntPtr.Zero;
    }

    private void HideAllVideoViews()
    {
        foreach (var videoView in _loadedVideoViews)
        {
            videoView.Visibility = Visibility.Hidden;
        }
    }

    private void UpdateVideoViewsVisibility()
    {
        _videoLayoutTimer.Stop();
        if (VideoScrollViewer == null || VideoItemsControl == null) return;

        double viewportHeight = VideoScrollViewer.ViewportHeight;
        double viewportWidth = VideoScrollViewer.ViewportWidth;

        foreach (var videoView in _loadedVideoViews.ToArray())
        {
            try
            {
                if (!videoView.IsLoaded) continue;
                if (!videoView.IsDescendantOf(VideoScrollViewer)) continue;

                // 检查 DataContext，如果未开始播放视频，则强制折叠，避免创建原生窗口或引起闪烁
                if (videoView.DataContext is ViewModels.VideoChannelItem vm)
                {
                    if (!vm.IsVideoViewVisible)
                    {
                        videoView.Visibility = Visibility.Hidden;
                        continue;
                    }
                }

                var transform = videoView.TransformToAncestor(VideoScrollViewer);
                var relativeRect = transform.TransformBounds(new Rect(0, 0, videoView.ActualWidth, videoView.ActualHeight));

                // 计算可视区域的相交矩形
                var intersection = Rect.Intersect(relativeRect, new Rect(0, 0, viewportWidth, viewportHeight));

                if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0)
                {
                    // 完全移出视口，隐藏它
                    videoView.Visibility = Visibility.Hidden;
                }
                else
                {
                    // 至少部分可见
                    videoView.Visibility = Visibility.Visible;

                    // 获取原生窗口句柄进行裁剪区设定 (解决 HwndHost 遮挡外侧控件的 airspace 问题)
                    IntPtr hwnd = GetHwnd(videoView);
                    if (hwnd != IntPtr.Zero)
                    {
                        // 计算相对于 VideoView 自己客户区的裁剪矩形 (WPF 逻辑像素)
                        double left = Math.Max(0, -relativeRect.Left);
                        double top = Math.Max(0, -relativeRect.Top);
                        double right = left + intersection.Width;
                        double bottom = top + intersection.Height;

                        // 转换为物理像素 (考虑系统 DPI 缩放)
                        var dpi = VisualTreeHelper.GetDpi(videoView);
                        int physLeft = (int)Math.Round(left * dpi.DpiScaleX);
                        int physTop = (int)Math.Round(top * dpi.DpiScaleY);
                        int physRight = (int)Math.Round(right * dpi.DpiScaleX);
                        int physBottom = (int)Math.Round(bottom * dpi.DpiScaleY);

                        // 设定原生窗口裁剪区
                        IntPtr hRgn = CreateRectRgn(physLeft, physTop, physRight, physBottom);
                        if (hRgn != IntPtr.Zero)
                        {
                            SetWindowRgn(hwnd, hRgn, true);
                        }
                    }
                }
            }
            catch
            {
                // Ignore if visual tree transforms fail (e.g. during disconnects/disposes)
            }
        }
    }

    private void RequestVideoViewsUpdate()
    {
        _videoLayoutTimer.Stop();
        _videoLayoutTimer.Start();
    }

    private void VideoView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is LibVLCSharp.WPF.VideoView videoView && videoView.MediaPlayer != null)
        {
            _loadedVideoViews.Add(videoView);
            // 防止重复订阅
            videoView.MediaPlayer.Playing -= MediaPlayer_Playing;
            videoView.MediaPlayer.Playing += MediaPlayer_Playing;
        }
        RequestVideoViewsUpdate();
    }

    private void VideoView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is LibVLCSharp.WPF.VideoView videoView)
        {
            _loadedVideoViews.Remove(videoView);
            if (videoView.MediaPlayer != null)
            {
                videoView.MediaPlayer.Playing -= MediaPlayer_Playing;
            }
        }
    }

    private async void MediaPlayer_Playing(object? sender, EventArgs e)
    {
        // 延迟 300 毫秒，确保 VLC 的 D3D 渲染链已经输出第一帧
        await System.Threading.Tasks.Task.Delay(300);
        
        Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            RequestVideoViewsUpdate();
        }));
    }

    private void VideoView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UIElement element && element.IsVisible)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                RequestVideoViewsUpdate();
            }));
        }
    }
}
