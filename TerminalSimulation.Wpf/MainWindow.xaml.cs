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
    private CancellationTokenSource? _mapSearchCancellation;
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
            await MapWebView.EnsureCoreWebView2Async(null);
            
            string mapHtml = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>Map</title>
    <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
    <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
    <script src='https://unpkg.com/leaflet-polylinedecorator/dist/leaflet.polylineDecorator.js'></script>
    <style>
        :root { color-scheme: light; --primary:#2196F3; --primary-dark:#1976D2; --text:#202124; --muted:#697386; --line:#e5e9ef; }
        * { box-sizing: border-box; }
        body { margin:0; padding:0; overflow:hidden; font-family:'Microsoft YaHei',sans-serif; color:var(--text); background:#222; }
        #map { width:100vw; height:100vh; transition:filter .5s ease; }
        #top-panel { position:absolute; top:16px; left:56px; z-index:1000; width:min(430px,calc(100vw - 76px)); max-height:calc(100vh - 32px); overflow:hidden; background:rgba(255,255,255,.97); border:1px solid rgba(255,255,255,.75); border-radius:16px; box-shadow:0 14px 40px rgba(25,42,70,.24),0 2px 8px rgba(25,42,70,.12); backdrop-filter:blur(18px); display:flex; flex-direction:column; }
        .panel-header { min-height:58px; padding:13px 16px; display:flex; align-items:center; justify-content:space-between; border-bottom:1px solid var(--line); }
        .panel-title { font-size:16px; font-weight:700; }
        .panel-subtitle { margin-top:3px; font-size:11px; color:var(--muted); }
        .panel-body { padding:14px; overflow-y:auto; overscroll-behavior:contain; }
        .panel-body.collapsed { display:none; }
        .icon-button { width:32px; height:32px; padding:0; border-radius:9px; background:#f1f4f8; color:#394554; font-size:18px; box-shadow:none; }
        .icon-button:hover { background:#e5eaf1; }
        .section { margin-bottom:12px; }
        .section:last-child { margin-bottom:0; }
        .section-title { margin:0 0 8px; display:flex; align-items:center; justify-content:space-between; font-size:12px; font-weight:700; color:#4a5565; }
        .search-row { display:grid; grid-template-columns:1fr auto; gap:8px; }
        .search-input { width:100%; height:40px; padding:0 12px; border:1px solid #d7dde5; border-radius:10px; outline:none; font-size:13px; background:#fff; }
        .search-input:focus { border-color:var(--primary); box-shadow:0 0 0 3px rgba(33,150,243,.12); }
        .primary-button,.secondary-button,.danger-button,.success-button { min-height:36px; padding:0 14px; border:0; border-radius:9px; cursor:pointer; font-size:13px; font-weight:600; transition:background .16s,transform .16s,box-shadow .16s; }
        .primary-button { color:#fff; background:var(--primary); box-shadow:0 4px 10px rgba(33,150,243,.22); }
        .primary-button:hover { background:var(--primary-dark); }
        .secondary-button { color:#334155; background:#eef2f6; }
        .secondary-button:hover { background:#e1e7ee; }
        .danger-button { color:#c62828; background:#ffebee; }
        .danger-button:hover { background:#ffcdd2; }
        .success-button { color:#fff; background:#2eaf62; box-shadow:0 4px 10px rgba(46,175,98,.22); }
        .success-button:hover { background:#228b4c; }
        button:disabled { opacity:.5; cursor:not-allowed; box-shadow:none; transform:none; }
        .search-results { margin-top:8px; max-height:250px; overflow-y:auto; border:1px solid var(--line); border-radius:12px; background:#fff; }
        .search-result { padding:10px 11px; border-bottom:1px solid #edf0f4; cursor:pointer; }
        .search-result:last-child { border-bottom:0; }
        .search-result:hover { background:#f6f9fc; }
        .result-title { font-size:13px; font-weight:700; line-height:1.35; }
        .result-address { margin-top:3px; color:var(--muted); font-size:11px; line-height:1.45; }
        .result-meta { margin-top:5px; color:#8a94a3; font-size:10px; }
        .result-actions { margin-top:8px; display:flex; gap:6px; }
        .result-actions button { min-height:28px; padding:0 9px; border-radius:7px; border:0; font-size:11px; cursor:pointer; }
        .route-card { padding:11px; border:1px solid var(--line); border-radius:12px; background:#f8fafc; }
        .route-point { display:grid; grid-template-columns:18px 1fr auto; gap:8px; align-items:center; min-height:38px; }
        .route-point > div { min-width:0; }
        .route-point + .route-point { border-top:1px dashed #dfe4ea; }
        .point-dot { width:10px; height:10px; border-radius:50%; justify-self:center; }
        .point-dot.start { background:#27ae60; box-shadow:0 0 0 4px rgba(39,174,96,.12); }
        .point-dot.end { background:#ef5350; box-shadow:0 0 0 4px rgba(239,83,80,.12); }
        .point-label { font-size:10px; color:#8a94a3; }
        .point-name { margin-top:1px; max-width:285px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; font-size:12px; font-weight:600; }
        .point-clear { border:0; background:transparent; color:#9aa3ae; cursor:pointer; font-size:16px; }
        .route-actions { display:grid; grid-template-columns:auto 1fr; gap:8px; margin-top:10px; }
        .route-summary { display:none; margin-top:9px; padding:8px 10px; border-radius:8px; background:#eaf5ff; color:#1769aa; font-size:11px; line-height:1.5; }
        .mode-row { display:flex; align-items:center; justify-content:space-between; gap:8px; }
        .switch-label { display:flex; align-items:center; gap:7px; font-size:12px; font-weight:600; }
        .switch-label input { width:auto; margin:0; accent-color:var(--primary); }
        .mode-tabs { display:grid; grid-template-columns:1fr 1fr; gap:4px; padding:4px; border-radius:11px; background:#edf1f5; }
        .mode-tab { min-height:34px; border:0; border-radius:8px; background:transparent; color:#667181; cursor:pointer; font-size:12px; font-weight:600; }
        .mode-tab.active { color:#147dcc; background:#fff; box-shadow:0 2px 8px rgba(36,52,72,.13); }
        .draw-status { display:flex; align-items:center; justify-content:space-between; gap:10px; }
        .draw-count { color:#1769aa; font-size:12px; font-weight:700; }
        .option-grid { display:grid; grid-template-columns:1fr 1fr; gap:8px; }
        .field { display:flex; flex-direction:column; gap:4px; min-width:0; color:#77808c; font-size:10px; }
        .field select,.field input { width:100%; height:34px; padding:0 9px; border:1px solid #dce1e7; border-radius:8px; outline:none; background:#fff; color:#283342; font-size:11px; }
        .simulation-actions { display:grid; grid-template-columns:auto 1fr; gap:8px; margin-top:10px; }
        .route-file-actions { display:grid; grid-template-columns:1fr 1fr; gap:8px; margin-top:8px; }
        .route-file-actions button { display:flex; align-items:center; justify-content:center; gap:6px; }
        .hint { margin-top:8px; color:#7a8491; font-size:10px; line-height:1.5; }
        .map-toast { position:fixed; left:50%; bottom:26px; z-index:2600; max-width:min(520px,calc(100vw - 32px)); padding:11px 16px; border-radius:11px; color:#fff; background:rgba(34,45,58,.94); box-shadow:0 10px 32px rgba(15,25,38,.28); font-size:12px; font-weight:600; opacity:0; visibility:hidden; transform:translate(-50%,14px); transition:opacity .2s ease,transform .2s ease,visibility .2s; pointer-events:none; }
        .map-toast.show { opacity:1; visibility:visible; transform:translate(-50%,0); }
        .map-toast.error { background:rgba(190,45,45,.96); }
        .settings-overlay { position:fixed; inset:0; z-index:2200; display:flex; align-items:center; justify-content:center; padding:20px; background:rgba(18,25,35,.32); backdrop-filter:blur(5px); opacity:0; visibility:hidden; pointer-events:none; transition:opacity .2s ease,visibility .2s ease; }
        .settings-overlay.open { opacity:1; visibility:visible; pointer-events:auto; }
        .settings-card { width:min(460px,calc(100vw - 32px)); max-height:calc(100vh - 40px); overflow:hidden; display:flex; flex-direction:column; border:1px solid rgba(255,255,255,.86); border-radius:20px; background:rgba(255,255,255,.98); box-shadow:0 24px 70px rgba(15,30,50,.3),0 4px 16px rgba(15,30,50,.14); transform:translateY(18px) scale(.97); transition:transform .24s cubic-bezier(.2,.8,.2,1); }
        .settings-overlay.open .settings-card { transform:translateY(0) scale(1); }
        .settings-header { display:flex; align-items:center; justify-content:space-between; padding:18px 20px 15px; border-bottom:1px solid var(--line); }
        .settings-heading { display:flex; align-items:center; gap:11px; }
        .settings-icon { width:38px; height:38px; display:grid; place-items:center; border-radius:12px; color:#147dcc; background:#e7f3ff; font-size:20px; }
        .settings-title { font-size:17px; font-weight:750; }
        .settings-subtitle { margin-top:2px; color:var(--muted); font-size:10px; }
        .settings-content { padding:18px 20px; overflow-y:auto; }
        .setting-item { display:grid; grid-template-columns:minmax(0,1fr) 190px; gap:18px; align-items:center; padding:13px 0; }
        .setting-item + .setting-item { border-top:1px solid #edf0f4; }
        .setting-name { font-size:13px; font-weight:700; }
        .setting-description { margin-top:4px; color:var(--muted); font-size:10px; line-height:1.45; }
        .setting-select { width:100%; height:38px; padding:0 10px; border:1px solid #d8dee6; border-radius:10px; outline:none; color:#263342; background:#fff; font-size:12px; }
        .setting-select:focus { border-color:var(--primary); box-shadow:0 0 0 3px rgba(33,150,243,.12); }
        .settings-actions { display:flex; justify-content:flex-end; gap:8px; padding:14px 20px 18px; border-top:1px solid var(--line); }
        .coordinate-flow { display:flex; align-items:center; justify-content:center; gap:10px; padding:14px 20px 2px; }
        .coordinate-badge { min-width:118px; padding:10px 12px; border:1px solid #dbe3ec; border-radius:11px; background:#f7f9fc; text-align:center; font-size:12px; font-weight:700; }
        .coordinate-arrow { color:#7f8b99; font-size:20px; }
        .coordinate-choices { display:grid; gap:10px; padding:16px 20px 4px; }
        .coordinate-choice { display:grid; grid-template-columns:38px 1fr; gap:12px; align-items:center; width:100%; padding:13px 14px; border:1px solid #dbe3ec; border-radius:13px; background:#fff; color:#263342; text-align:left; cursor:pointer; box-shadow:none; }
        .coordinate-choice:hover { border-color:#90caf9; background:#f5faff; transform:translateY(-1px); }
        .coordinate-choice-icon { width:38px; height:38px; display:grid; place-items:center; border-radius:11px; color:#147dcc; background:#e8f4ff; font-size:18px; }
        .coordinate-choice-title { font-size:13px; font-weight:700; }
        .coordinate-choice-description { margin-top:3px; color:var(--muted); font-size:10px; line-height:1.45; }
        @media (max-width:650px) { #top-panel { left:12px; top:12px; width:calc(100vw - 24px); max-height:calc(100vh - 24px); } .leaflet-control-zoom { display:none; } }
        @media (max-width:520px) { .setting-item { grid-template-columns:1fr; gap:8px; } .settings-card { max-height:calc(100vh - 24px); } }
        /* Map Styles */
        .map-style-default { filter: none; }
        .map-style-dark { filter: invert(100%) hue-rotate(180deg) brightness(95%) contrast(90%); }
        .map-style-navy { filter: invert(100%) hue-rotate(200deg) sepia(20%) brightness(110%) contrast(90%); }
        .map-style-emerald { filter: invert(100%) hue-rotate(260deg) sepia(40%) saturate(150%) brightness(95%) contrast(90%); }
        .map-style-grayscale { filter: grayscale(100%) brightness(110%) contrast(90%); }
        
        /* Path Themes */
        .path-line { transition: stroke 0.3s, filter 0.3s; }
        .path-arrow { transition: fill 0.3s, stroke 0.3s, filter 0.3s; }
        .car-arrow-svg { transition: fill 0.3s, filter 0.3s; }
        
        .theme-neon-blue .path-line { stroke: #00E5FF !important; filter: drop-shadow(0 0 4px #00E5FF) !important; }
        .theme-neon-blue .path-arrow { fill: #00E5FF !important; stroke: #00E5FF !important; filter: drop-shadow(0 0 4px #00E5FF) !important; }
        .theme-neon-blue .car-arrow-svg { fill: #FF0055 !important; filter: drop-shadow(0 0 5px #FF0055) !important; }

        .theme-neon-purple .path-line { stroke: #B026FF !important; filter: drop-shadow(0 0 4px #B026FF) !important; }
        .theme-neon-purple .path-arrow { fill: #B026FF !important; stroke: #B026FF !important; filter: drop-shadow(0 0 4px #B026FF) !important; }
        .theme-neon-purple .car-arrow-svg { fill: #00FFCC !important; filter: drop-shadow(0 0 5px #00FFCC) !important; }

        .theme-solid-red .path-line { stroke: #4CAF50 !important; filter: none !important; }
        .theme-solid-red .path-arrow { fill: #4CAF50 !important; stroke: #4CAF50 !important; filter: none !important; }
        .theme-solid-red .car-arrow-svg { fill: #F44336 !important; filter: none !important; }

        .theme-solid-orange .path-line { stroke: #2196F3 !important; filter: none !important; }
        .theme-solid-orange .path-arrow { fill: #2196F3 !important; stroke: #2196F3 !important; filter: none !important; }
        .theme-solid-orange .car-arrow-svg { fill: #FF9800 !important; filter: none !important; }

        .car-arrow { transition:transform .1s linear; }
    </style>
</head>
<body class=""theme-neon-blue"">
    <div id='top-panel'>
        <div class='panel-header'>
            <div><div class='panel-title'>地图选点与路线模拟</div><div class='panel-subtitle'>搜索地点、规划道路导航并模拟位置上报</div></div>
            <div style='display:flex;gap:6px;'><button id='locateButton' class='icon-button' onclick='locateDevicePosition(true)' title='定位到设备网络位置'>◎</button><button class='icon-button' onclick='openMapSettings()' title='地图选点设置'>⚙</button><button id='panelToggle' class='icon-button' onclick='togglePanel()' title='收起面板'>−</button></div>
        </div>
        <div id='panelBody' class='panel-body'>
            <div class='section'>
                <div class='section-title'><span>地点搜索</span><span id='searchStatus'></span></div>
                <div class='search-row'>
                    <input class='search-input' type='text' id='searchInput' autocomplete='off' placeholder='输入地点、道路、建筑或完整地址' oninput='updateSearchHint()' onkeydown='if(event.key===""Enter""){event.preventDefault();search();}' />
                    <button class='primary-button' onclick='search()'>搜索</button>
                </div>
                <div id='searchResults' class='search-results' style='display:none;'></div>
            </div>
            <div class='section'>
                <div class='section-title'>路线创建方式</div>
                <div class='mode-tabs'><button id='modeDraw' class='mode-tab' onclick='setRouteMode(""draw"")'>✎ 自由绘制</button><button id='modeNavigation' class='mode-tab active' onclick='setRouteMode(""navigation"")'>➤ 导航规划</button></div>
            </div>
            <div id='navigationSection' class='section route-card'>
                <div class='section-title'><span>导航路线</span><span id='routeState'>等待选择</span></div>
                <div class='route-point'><span class='point-dot start'></span><div><div class='point-label'>起点</div><div id='startPointName' class='point-name'>尚未设置</div></div><button class='point-clear' onclick='clearRoutePoint(""start"")' title='清除起点'>×</button></div>
                <div class='route-point'><span class='point-dot end'></span><div><div class='point-label'>终点</div><div id='endPointName' class='point-name'>尚未设置</div></div><button class='point-clear' onclick='clearRoutePoint(""end"")' title='清除终点'>×</button></div>
                <div class='route-actions'><button class='secondary-button' onclick='swapRoutePoints()' title='交换起点和终点'>⇅ 交换</button><button id='btnPlan' class='primary-button' onclick='planNavigationRoute()' disabled>规划驾车路线</button></div>
                <div id='routeSummary' class='route-summary'></div>
            </div>
            <div id='drawSection' class='section route-card' style='display:none;'>
                <div class='section-title'><span>自由绘制路线</span><span id='drawState'>等待绘制</span></div>
                <div class='draw-status'><div><div class='point-label'>使用方法</div><div class='point-name'>在地图上依次点击添加轨迹点</div></div><div id='drawPointCount' class='draw-count'>0 个点</div></div>
                <div class='route-actions'><button id='btnUndoDraw' class='secondary-button' onclick='undoDrawPoint()' disabled>撤销上一点</button><button class='secondary-button' onclick='clearPath()'>重新绘制</button></div>
            </div>
            <div class='section'>
                <div class='section-title'>模拟设置</div>
                <div class='option-grid'>
                    <label class='field'>坐标系<select id='coordinateSystem' onchange='requestCoordinateSystemChange()'><option value='gcj02'>高德 GCJ-02</option><option value='wgs84'>GPS WGS-84</option><option value='bd09'>百度 BD-09</option></select></label>
                    <label class='field'>模拟速度（km/h）<input type='number' id='simSpeed' value='60.5' min='1' max='300' step='0.1' onchange='changeSpeed()' /></label>
                </div>
                <div class='simulation-actions'><button id='btnClear' class='secondary-button' onclick='clearPath()'>清空路线</button><button id='btnStart' class='success-button' onclick='startSimulation()' disabled>开始模拟上报</button><button id='btnStop' class='danger-button' onclick='stopSimulation()' style='display:none;'>中断模拟</button></div>
                <div class='route-file-actions'><button id='btnOpenRoute' class='secondary-button' onclick='openRouteFile()'>↗ 打开路线</button><button id='btnSaveRoute' class='secondary-button' onclick='saveRouteFile()' disabled>↓ 保存路线</button></div>
                <div class='hint'>可在自由绘制与导航规划之间切换；路线文件、模拟和 JT/T 808 上报始终使用当前选择的同一坐标系。高德底图仅在显示时进行必要换算。初始视野来自公网 IP 的城市级粗略定位。地点数据 © OpenStreetMap contributors，路线由 OSRM 提供。</div>
            </div>
        </div>
    </div>
    <div id='settingsOverlay' class='settings-overlay' onclick='if(event.target===this)closeMapSettings()'>
        <div class='settings-card' role='dialog' aria-modal='true' aria-labelledby='mapSettingsTitle'>
            <div class='settings-header'>
                <div class='settings-heading'><div class='settings-icon'>⚙</div><div><div id='mapSettingsTitle' class='settings-title'>地图选点设置</div><div class='settings-subtitle'>仅影响地图选点页面的显示和路线外观</div></div></div>
                <button class='icon-button' onclick='closeMapSettings()' title='关闭'>×</button>
            </div>
            <div class='settings-content'>
                <div class='setting-item'><div><div class='setting-name'>默认位置</div><div class='setting-description'>打开地图选点时显示的位置；自动模式使用设备公网 IP 的城市级位置。</div></div><select id='settingsDefaultLocation' class='setting-select'><option value='auto'>自动（网络位置）</option><option value='beijing'>北京</option><option value='shanghai'>上海</option><option value='guangzhou'>广州</option><option value='shenzhen'>深圳</option><option value='chengdu'>成都</option><option value='chongqing'>重庆</option><option value='hangzhou'>杭州</option><option value='wuhan'>武汉</option><option value='xian'>西安</option><option value='nanjing'>南京</option><option value='tianjin'>天津</option><option value='suzhou'>苏州</option><option value='changsha'>长沙</option><option value='zhengzhou'>郑州</option><option value='qingdao'>青岛</option><option value='xiamen'>厦门</option><option value='jinan'>济南</option><option value='shenyang'>沈阳</option><option value='harbin'>哈尔滨</option><option value='kunming'>昆明</option><option value='fuzhou'>福州</option><option value='hefei'>合肥</option><option value='nanchang'>南昌</option><option value='nanning'>南宁</option><option value='guiyang'>贵阳</option><option value='urumqi'>乌鲁木齐</option><option value='lhasa'>拉萨</option><option value='hohhot'>呼和浩特</option><option value='haikou'>海口</option><option value='lanzhou'>兰州</option><option value='yinchuan'>银川</option><option value='xining'>西宁</option><option value='taiyuan'>太原</option><option value='shijiazhuang'>石家庄</option><option value='changchun'>长春</option></select></div>
                <div class='setting-item'><div><div class='setting-name'>地图风格</div><div class='setting-description'>调整地图底图的整体色彩，标记和路线保持清晰可见。</div></div><select id='settingsMapStyle' class='setting-select'><option value='map-style-default'>默认高德</option><option value='map-style-dark'>幻影黑</option><option value='map-style-navy'>极夜蓝</option><option value='map-style-emerald'>墨绿雷达</option><option value='map-style-grayscale'>黑白灰</option></select></div>
                <div class='setting-item'><div><div class='setting-name'>路线样式</div><div class='setting-description'>同时应用到自由绘制、导航路线和模拟车辆指示。</div></div><select id='settingsPathStyle' class='setting-select'><option value='theme-neon-blue'>霓虹青蓝</option><option value='theme-neon-purple'>赛博紫粉</option><option value='theme-solid-red'>绿线红车</option><option value='theme-solid-orange'>蓝线橙车</option></select></div>
            </div>
            <div class='settings-actions'><button class='secondary-button' onclick='resetMapSettingsDraft()'>恢复默认</button><button class='secondary-button' onclick='closeMapSettings()'>取消</button><button class='primary-button' onclick='applyMapSettings()'>保存并应用</button></div>
        </div>
    </div>
    <div id='coordinateOverlay' class='settings-overlay' onclick='if(event.target===this)cancelCoordinateSystemChange()'>
        <div class='settings-card' role='dialog' aria-modal='true' aria-labelledby='coordinateDialogTitle'>
            <div class='settings-header'>
                <div class='settings-heading'><div class='settings-icon'>⌖</div><div><div id='coordinateDialogTitle' class='settings-title'>切换坐标系</div><div class='settings-subtitle'>请选择已有坐标与路线的处理方式</div></div></div>
                <button class='icon-button' onclick='cancelCoordinateSystemChange()' title='取消切换'>×</button>
            </div>
            <div class='coordinate-flow'><div id='coordinateFrom' class='coordinate-badge'></div><div class='coordinate-arrow'>→</div><div id='coordinateTo' class='coordinate-badge'></div></div>
            <div class='coordinate-choices'>
                <button class='coordinate-choice' onclick='applyCoordinateSystemChange(false)'><span class='coordinate-choice-icon'>⇄</span><span><div class='coordinate-choice-title'>直接切换</div><div class='coordinate-choice-description'>保留所有数值不变，仅改变坐标系解释；地图上的点可能发生位移。</div></span></button>
                <button class='coordinate-choice' onclick='applyCoordinateSystemChange(true)'><span class='coordinate-choice-icon'>◎</span><span><div class='coordinate-choice-title'>保持地图位置并转换坐标</div><div class='coordinate-choice-description'>转换已有坐标数值，使点与路线在地图上的实际位置保持不变。</div></span></button>
            </div>
            <div class='settings-actions'><button class='secondary-button' onclick='cancelCoordinateSystemChange()'>取消</button></div>
        </div>
    </div>
    <div id='mapToast' class='map-toast' role='status' aria-live='polite'></div>
    <div id='map'></div>
    <script>
        var map = L.map('map').setView([35.8617, 104.1954], 4);
        L.tileLayer('https://webrd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=8&x={x}&y={y}&z={z}', {
            subdomains: ['1', '2', '3', '4'],
            attribution: '© 高德地图'
        }).addTo(map);

        var marker;
        var markerPoint = null;
        var carMarker;
        var carPoint = null;
        var carDirection = 0;
        var networkLocationMarker;
        var networkLocationAccuracyCircle;
        var networkLocationWgs = null;
        var networkLocationRequestedManually = false;
        var routeMode = 'navigation';
        var isSimulating = false;
        var pathPoints = [];
        var routeStart = null;
        var routeEnd = null;
        var searchResults = [];
        var searchController = null;
        var routeController = null;
        // 逻辑坐标系同时用于路线文件、模拟和 JT/T 808 上报；高德瓦片显示层固定为 GCJ-02。
        var coordinateSystem = 'gcj02';
        var pendingCoordinateSystem = null;
        var mapDefaultLocation = 'auto';
        var currentMapStyle = 'map-style-default';
        var currentPathStyle = 'theme-neon-blue';
        var defaultCities = {
            beijing:{name:'北京',lat:39.9042,lng:116.4074}, shanghai:{name:'上海',lat:31.2304,lng:121.4737}, guangzhou:{name:'广州',lat:23.1291,lng:113.2644}, shenzhen:{name:'深圳',lat:22.5431,lng:114.0579},
            chengdu:{name:'成都',lat:30.5728,lng:104.0668}, chongqing:{name:'重庆',lat:29.5630,lng:106.5516}, hangzhou:{name:'杭州',lat:30.2741,lng:120.1551}, wuhan:{name:'武汉',lat:30.5928,lng:114.3055},
            xian:{name:'西安',lat:34.3416,lng:108.9398}, nanjing:{name:'南京',lat:32.0603,lng:118.7969}, tianjin:{name:'天津',lat:39.0842,lng:117.2009}, suzhou:{name:'苏州',lat:31.2989,lng:120.5853},
            changsha:{name:'长沙',lat:28.2282,lng:112.9388}, zhengzhou:{name:'郑州',lat:34.7466,lng:113.6254}, qingdao:{name:'青岛',lat:36.0671,lng:120.3826}, xiamen:{name:'厦门',lat:24.4798,lng:118.0894},
            jinan:{name:'济南',lat:36.6512,lng:117.1201}, shenyang:{name:'沈阳',lat:41.8057,lng:123.4315}, harbin:{name:'哈尔滨',lat:45.8038,lng:126.5349}, kunming:{name:'昆明',lat:25.0389,lng:102.7183},
            fuzhou:{name:'福州',lat:26.0745,lng:119.2965}, hefei:{name:'合肥',lat:31.8206,lng:117.2272}, nanchang:{name:'南昌',lat:28.6820,lng:115.8579}, nanning:{name:'南宁',lat:22.8170,lng:108.3669},
            guiyang:{name:'贵阳',lat:26.6470,lng:106.6302}, urumqi:{name:'乌鲁木齐',lat:43.8256,lng:87.6168}, lhasa:{name:'拉萨',lat:29.6520,lng:91.1721}, hohhot:{name:'呼和浩特',lat:40.8426,lng:111.7492},
            haikou:{name:'海口',lat:20.0440,lng:110.1999}, lanzhou:{name:'兰州',lat:36.0611,lng:103.8343}, yinchuan:{name:'银川',lat:38.4872,lng:106.2309}, xining:{name:'西宁',lat:36.6171,lng:101.7782},
            taiyuan:{name:'太原',lat:37.8706,lng:112.5489}, shijiazhuang:{name:'石家庄',lat:38.0428,lng:114.5149}, changchun:{name:'长春',lat:43.8171,lng:125.3235}
        };
        var polyline = L.polyline([], {weight: 4, className: 'path-line'}).addTo(map);
        var startMarker, endMarker;
        var decorator;

        map.createPane('arrowPane');
        map.getPane('arrowPane').style.zIndex = 450;
        map.getPane('arrowPane').style.pointerEvents = 'none';

        function changeMapStyle(value) {
            var mapEl = document.getElementById('map');
            currentMapStyle = value || currentMapStyle;
            mapEl.className = currentMapStyle;
        }

        function changePathStyle(value) {
            var bodyEl = document.body;
            bodyEl.classList.remove('theme-neon-blue', 'theme-neon-purple', 'theme-solid-red', 'theme-solid-orange');
            currentPathStyle = value || currentPathStyle;
            bodyEl.classList.add(currentPathStyle);
        }

        function openMapSettings() {
            document.getElementById('settingsDefaultLocation').value = mapDefaultLocation;
            document.getElementById('settingsMapStyle').value = currentMapStyle;
            document.getElementById('settingsPathStyle').value = currentPathStyle;
            document.getElementById('settingsOverlay').classList.add('open');
        }

        function closeMapSettings() {
            document.getElementById('settingsOverlay').classList.remove('open');
        }

        function resetMapSettingsDraft() {
            document.getElementById('settingsDefaultLocation').value = 'auto';
            document.getElementById('settingsMapStyle').value = 'map-style-default';
            document.getElementById('settingsPathStyle').value = 'theme-neon-blue';
        }

        function applyMapSettings() {
            mapDefaultLocation = document.getElementById('settingsDefaultLocation').value;
            currentMapStyle = document.getElementById('settingsMapStyle').value;
            currentPathStyle = document.getElementById('settingsPathStyle').value;
            changeMapStyle(currentMapStyle);
            changePathStyle(currentPathStyle);
            focusDefaultLocation();
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({type:'save_map_settings',defaultLocation:mapDefaultLocation,mapStyle:currentMapStyle,pathStyle:currentPathStyle}));
            }
            closeMapSettings();
        }

        function initializeMapSettings(settings) {
            var locationValue = settings && settings.defaultLocation;
            mapDefaultLocation = locationValue === 'auto' || defaultCities[locationValue] ? locationValue : 'auto';
            var mapStyleSelect = document.getElementById('settingsMapStyle');
            var pathStyleSelect = document.getElementById('settingsPathStyle');
            currentMapStyle = settings && Array.from(mapStyleSelect.options).some(function(option){return option.value === settings.mapStyle;}) ? settings.mapStyle : 'map-style-default';
            currentPathStyle = settings && Array.from(pathStyleSelect.options).some(function(option){return option.value === settings.pathStyle;}) ? settings.pathStyle : 'theme-neon-blue';
            changeMapStyle(currentMapStyle);
            changePathStyle(currentPathStyle);
            focusDefaultLocation();
        }

        function requestMapSettings() {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({type:'request_map_settings'}));
            } else {
                initializeMapSettings({defaultLocation:'auto',mapStyle:'map-style-default',pathStyle:'theme-neon-blue'});
            }
        }

        function focusDefaultLocation() {
            if (mapDefaultLocation === 'auto') {
                if (networkLocationWgs) {
                    showNetworkLocation(networkLocationWgs);
                    var networkDisplay = convertCoordinate(networkLocationWgs.lat,networkLocationWgs.lng,'wgs84','gcj02');
                    map.setView([networkDisplay.lat,networkDisplay.lng],11);
                    return;
                }
                locateDevicePosition(false);
                return;
            }
            var city = defaultCities[mapDefaultLocation];
            if (!city) return;
            if (networkLocationMarker) { map.removeLayer(networkLocationMarker); networkLocationMarker = null; }
            if (networkLocationAccuracyCircle) { map.removeLayer(networkLocationAccuracyCircle); networkLocationAccuracyCircle = null; }
            var display = convertCoordinate(city.lat,city.lng,'wgs84','gcj02');
            map.setView([display.lat,display.lng],12);
            postMapLog('地图默认位置已切换为：' + city.name);
        }

        var mapToastTimer = null;
        function showMapToast(message, isError) {
            var toast = document.getElementById('mapToast');
            toast.textContent = message || '';
            toast.classList.toggle('error',!!isError);
            toast.classList.add('show');
            if (mapToastTimer) clearTimeout(mapToastTimer);
            mapToastTimer = setTimeout(function(){ toast.classList.remove('show'); },3200);
        }

        function updateRouteFileActions() {
            document.getElementById('btnSaveRoute').disabled = isSimulating || pathPoints.length < 2;
            document.getElementById('btnOpenRoute').disabled = isSimulating;
        }

        function copyRoutePoint(point) {
            if (!point) return null;
            return {lat:Number(point.lat),lng:Number(point.lng),name:point.name || ''};
        }

        function saveRouteFile() {
            if (isSimulating || pathPoints.length < 2) {
                showMapToast('请先创建至少包含两个轨迹点的路线。',true);
                return;
            }
            if (!window.chrome || !window.chrome.webview) {
                showMapToast('当前环境不支持保存路线。',true);
                return;
            }
            var route = {
                mode:routeMode,
                coordinateSystem:coordinateSystem,
                speed:parseFloat(document.getElementById('simSpeed').value) || 60.5,
                points:pathPoints.map(function(point){return copyRoutePoint(point);}),
                start:copyRoutePoint(routeStart),
                end:copyRoutePoint(routeEnd)
            };
            window.chrome.webview.postMessage(JSON.stringify({type:'save_map_route',route:route}));
        }

        function openRouteFile() {
            if (isSimulating) return;
            if (!window.chrome || !window.chrome.webview) {
                showMapToast('当前环境不支持打开路线。',true);
                return;
            }
            window.chrome.webview.postMessage(JSON.stringify({type:'open_map_route'}));
        }

        function loadRouteFile(route) {
            if (isSimulating || !route || !Array.isArray(route.points) || route.points.length < 2) {
                showMapToast('路线文件没有有效轨迹点。',true);
                return;
            }
            var fileCoordinateSystem = route.coordinateSystem || 'wgs84';
            var mode = route.mode === 'navigation' ? 'navigation' : 'draw';
            setRouteMode(mode);
            clearPath();
            coordinateSystem = fileCoordinateSystem;
            document.getElementById('coordinateSystem').value = fileCoordinateSystem;
            if (networkLocationWgs) showNetworkLocation(networkLocationWgs);
            var loadedPoints = route.points.map(function(point){return {lat:Number(point.lat),lng:Number(point.lng),name:point.name || ''};});
            if (mode === 'navigation') {
                var rawStart = route.start || route.points[0];
                var rawEnd = route.end || route.points[route.points.length - 1];
                var loadedStart = {lat:Number(rawStart.lat),lng:Number(rawStart.lng)};
                var loadedEnd = {lat:Number(rawEnd.lat),lng:Number(rawEnd.lng)};
                setRoutePoint('start',loadedStart.lat,loadedStart.lng,rawStart.name || '保存路线起点');
                setRoutePoint('end',loadedEnd.lat,loadedEnd.lng,rawEnd.name || '保存路线终点');
                pathPoints = loadedPoints;
                renderPathPolyline();
                drawPathDecorations();
                updateRouteUi();
                document.getElementById('routeState').textContent = '已打开';
                var summary = document.getElementById('routeSummary');
                summary.style.display = 'block';
                summary.textContent = '已从路线文件加载，共 ' + pathPoints.length + ' 个轨迹点';
            } else {
                pathPoints = loadedPoints;
                renderDrawPath();
            }
            if (Number.isFinite(Number(route.speed))) document.getElementById('simSpeed').value = Number(route.speed);
            if (polyline.getBounds().isValid()) map.fitBounds(polyline.getBounds(),{padding:[48,48],maxZoom:16});
            document.getElementById('btnStart').disabled = false;
            updateRouteFileActions();
            showMapToast('路线已打开并切换为 ' + coordinateSystemLabel(coordinateSystem) + '：' + pathPoints.length + ' 个轨迹点');
            postMapLog('已打开路线文件；路径、模拟及 JT/T 808 上报统一切换为 ' + coordinateSystemLabel(coordinateSystem));
        }

        function routeFileOperationResult(operation, succeeded, message) {
            showMapToast(message || (succeeded ? '操作完成' : '操作失败'),!succeeded);
        }

        function setRouteMode(mode) {
            if (isSimulating || (mode !== 'draw' && mode !== 'navigation')) return;
            if (mode !== routeMode) clearPath();
            routeMode = mode;
            document.getElementById('modeDraw').classList.toggle('active',mode === 'draw');
            document.getElementById('modeNavigation').classList.toggle('active',mode === 'navigation');
            document.getElementById('navigationSection').style.display = mode === 'navigation' ? 'block' : 'none';
            document.getElementById('drawSection').style.display = mode === 'draw' ? 'block' : 'none';
            document.getElementById('btnStart').disabled = pathPoints.length < 2;
            if (mode === 'draw') updateDrawUi(); else updateRouteUi();
        }

        function togglePanel() {
            var body = document.getElementById('panelBody');
            var button = document.getElementById('panelToggle');
            body.classList.toggle('collapsed');
            var collapsed = body.classList.contains('collapsed');
            button.textContent = collapsed ? '+' : '−';
            button.title = collapsed ? '展开面板' : '收起面板';
        }

        function coordinateSystemLabel(value) {
            return value === 'wgs84' ? 'GPS WGS-84' : (value === 'bd09' ? '百度 BD-09' : '高德 GCJ-02');
        }

        function requestCoordinateSystemChange() {
            if (isSimulating) {
                document.getElementById('coordinateSystem').value = coordinateSystem;
                return;
            }
            var next = document.getElementById('coordinateSystem').value;
            if (next === coordinateSystem) return;
            document.getElementById('coordinateSystem').value = coordinateSystem;
            pendingCoordinateSystem = next;
            document.getElementById('coordinateFrom').textContent = coordinateSystemLabel(coordinateSystem);
            document.getElementById('coordinateTo').textContent = coordinateSystemLabel(next);
            document.getElementById('coordinateOverlay').classList.add('open');
        }

        function cancelCoordinateSystemChange() {
            pendingCoordinateSystem = null;
            document.getElementById('coordinateSystem').value = coordinateSystem;
            document.getElementById('coordinateOverlay').classList.remove('open');
        }

        function applyCoordinateSystemChange(convertExistingPoints) {
            var next = pendingCoordinateSystem;
            if (!next || next === coordinateSystem) {
                cancelCoordinateSystemChange();
                return;
            }
            var previous = coordinateSystem;
            if (convertExistingPoints) {
                if (markerPoint) markerPoint = convertCoordinate(markerPoint.lat,markerPoint.lng,previous,next);
                if (routeStart) {
                    var convertedStart = convertCoordinate(routeStart.lat,routeStart.lng,previous,next);
                    convertedStart.name = routeStart.name;
                    routeStart = convertedStart;
                }
                if (routeEnd) {
                    var convertedEnd = convertCoordinate(routeEnd.lat,routeEnd.lng,previous,next);
                    convertedEnd.name = routeEnd.name;
                    routeEnd = convertedEnd;
                }
                pathPoints = pathPoints.map(function(point) {
                    var converted = convertCoordinate(point.lat,point.lng,previous,next);
                    converted.name = point.name || '';
                    return converted;
                });
            }
            coordinateSystem = next;
            pendingCoordinateSystem = null;
            document.getElementById('coordinateSystem').value = coordinateSystem;
            document.getElementById('coordinateOverlay').classList.remove('open');
            renderAllLogicalGeometry();
            if (networkLocationWgs) showNetworkLocation(networkLocationWgs);
            if (pathPoints.length >= 2) {
                var summary = document.getElementById('routeSummary');
                if (routeMode === 'navigation') {
                    summary.style.display = 'block';
                    summary.textContent = (convertExistingPoints ? '已转换坐标并保持地图位置' : '已直接切换坐标解释') + '，路线与上报均使用 ' + coordinateSystemLabel(coordinateSystem);
                    document.getElementById('routeState').textContent = '已切换';
                }
            }
            var actionText = convertExistingPoints ? '已转换已有坐标，地图位置保持不变' : '已直接切换，坐标数值保持不变';
            showMapToast(actionText + '；路线和上报统一使用 ' + coordinateSystemLabel(coordinateSystem));
            postMapLog(actionText + '；路径、模拟及 JT/T 808 上报统一使用 ' + coordinateSystemLabel(coordinateSystem));
        }

        function outOfChina(lat, lon) {
            return lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271;
        }
        function transformLat(x, y) {
            var ret = -100 + 2*x + 3*y + 0.2*y*y + 0.1*x*y + 0.2*Math.sqrt(Math.abs(x));
            ret += (20*Math.sin(6*x*Math.PI) + 20*Math.sin(2*x*Math.PI))*2/3;
            ret += (20*Math.sin(y*Math.PI) + 40*Math.sin(y/3*Math.PI))*2/3;
            ret += (160*Math.sin(y/12*Math.PI) + 320*Math.sin(y*Math.PI/30))*2/3;
            return ret;
        }
        function transformLon(x, y) {
            var ret = 300 + x + 2*y + 0.1*x*x + 0.1*x*y + 0.1*Math.sqrt(Math.abs(x));
            ret += (20*Math.sin(6*x*Math.PI) + 20*Math.sin(2*x*Math.PI))*2/3;
            ret += (20*Math.sin(x*Math.PI) + 40*Math.sin(x/3*Math.PI))*2/3;
            ret += (150*Math.sin(x/12*Math.PI) + 300*Math.sin(x/30*Math.PI))*2/3;
            return ret;
        }
        function wgs84ToGcj02(lat, lon) {
            if (outOfChina(lat, lon)) return {lat:lat, lng:lon};
            var ee = 0.00669342162296594323, a = 6378245.0, dLat = transformLat(lon-105, lat-35);
            var dLon = transformLon(lon-105, lat-35), radLat = lat/180*Math.PI;
            var magic = 1-ee*Math.sin(radLat)*Math.sin(radLat), sqrtMagic = Math.sqrt(magic);
            dLat = dLat*180/((a*(1-ee))/(magic*sqrtMagic)*Math.PI);
            dLon = dLon*180/(a/sqrtMagic*Math.cos(radLat)*Math.PI);
            return {lat:lat+dLat, lng:lon+dLon};
        }
        function gcj02ToWgs84(lat, lon) {
            if (outOfChina(lat, lon)) return {lat:lat, lng:lon};
            var p = wgs84ToGcj02(lat, lon);
            return {lat:lat*2-p.lat, lng:lon*2-p.lng};
        }
        function gcj02ToBd09(lat, lon) {
            var x = lon, y = lat, xp = Math.PI*3000/180, z = Math.sqrt(x*x+y*y)+0.00002*Math.sin(y*xp);
            var theta = Math.atan2(y,x)+0.000003*Math.cos(x*xp);
            return {lat:z*Math.sin(theta)+0.006, lng:z*Math.cos(theta)+0.0065};
        }
        function bd09ToGcj02(lat, lon) {
            var x = lon-0.0065, y = lat-0.006, xp = Math.PI*3000/180, z = Math.sqrt(x*x+y*y)-0.00002*Math.sin(y*xp);
            var theta = Math.atan2(y,x)-0.000003*Math.cos(x*xp);
            return {lat:z*Math.sin(theta), lng:z*Math.cos(theta)};
        }
        function convertCoordinate(lat, lng, from, to) {
            if (from === to) return {lat:lat, lng:lng};
            var wgs = from === 'wgs84' ? {lat:lat,lng:lng} : (from === 'gcj02' ? gcj02ToWgs84(lat,lng) : gcj02ToWgs84(bd09ToGcj02(lat,lng).lat, bd09ToGcj02(lat,lng).lng));
            return to === 'wgs84' ? wgs : (to === 'gcj02' ? wgs84ToGcj02(wgs.lat,wgs.lng) : gcj02ToBd09(wgs84ToGcj02(wgs.lat,wgs.lng).lat,wgs84ToGcj02(wgs.lat,wgs.lng).lng));
        }

        // 高德瓦片固定采用 GCJ-02。pathPoints、routeStart、routeEnd 和 markerPoint
        // 始终保存为用户选择的逻辑坐标系，并原样交给模拟及 JT/T 808 上报。
        function logicalToDisplayPoint(point, system) {
            return convertCoordinate(Number(point.lat),Number(point.lng),system || coordinateSystem,'gcj02');
        }

        function displayToLogicalPoint(lat, lng, system) {
            return convertCoordinate(Number(lat),Number(lng),'gcj02',system || coordinateSystem);
        }

        function renderPathPolyline() {
            polyline.setLatLngs(pathPoints.map(function(point) {
                var display = logicalToDisplayPoint(point);
                return [display.lat,display.lng];
            }));
        }

        function renderAllLogicalGeometry() {
            if (markerPoint) placeMarker(markerPoint.lat,markerPoint.lng);
            renderPathPolyline();
            if (decorator) { map.removeLayer(decorator); decorator = null; }
            if (routeMode === 'navigation') {
                refreshNavigationMarkers();
                updateRouteUi();
            } else {
                refreshDrawMarkers();
                updateDrawUi();
            }
            drawPathDecorations();
            if (carPoint) updateCarLocationLogical(carPoint.lat,carPoint.lng,carDirection);
            if (searchResults.length) renderSearchResults();
            document.getElementById('btnStart').disabled = pathPoints.length < 2 || isSimulating;
            updateRouteFileActions();
        }

        map.on('click', function(e) {
            if (isSimulating) return; // ignore clicks during simulation

            var logical = displayToLogicalPoint(e.latlng.lat,e.latlng.lng);
            var lat = logical.lat;
            var lng = logical.lng;

            if (routeMode === 'draw') {
                addDrawPoint(lat, lng);
            } else {
                if (!routeStart || (routeStart && routeEnd)) {
                    setRoutePoint('start', lat, lng, '地图选点');
                    if (routeEnd) clearRoutePoint('end');
                } else {
                    setRoutePoint('end', lat, lng, '地图选点');
                    planNavigationRoute();
                }
            }
        });

        function placeMarker(lat, lng) {
            markerPoint = {lat:Number(lat),lng:Number(lng)};
            var display = logicalToDisplayPoint(markerPoint);
            if (marker) map.removeLayer(marker);
            marker = L.marker([display.lat, display.lng]).addTo(map)
                .bindPopup('<div style=""text-align:center;min-width:190px;""><b>当前坐标</b><br>纬度: ' + markerPoint.lat.toFixed(6) + '<br>经度: ' + markerPoint.lng.toFixed(6) +
                           '<br>坐标系: ' + coordinateSystem.toUpperCase() + '<br><div style=""display:flex;gap:5px;justify-content:center;margin-top:8px;""><button onclick=""applyCoords(' + markerPoint.lat + ',' + markerPoint.lng + ')"">应用坐标</button><button onclick=""chooseNavigationPoint(&#39;start&#39;,' + markerPoint.lat + ',' + markerPoint.lng + ',&#39;地图选点&#39;)"">设为起点</button><button onclick=""chooseNavigationPoint(&#39;end&#39;,' + markerPoint.lat + ',' + markerPoint.lng + ',&#39;地图选点&#39;)"">设为终点</button></div></div>')
                .openPopup();
        }

        function clearPath() {
            if (isSimulating) return;
            if (routeMode === 'navigation') {
                clearRoutePoint('start', true);
                clearRoutePoint('end', true);
                removeRouteGeometry();
                updateRouteUi();
                document.getElementById('routeState').textContent = '等待选择';
            } else {
                removeRouteGeometry();
                if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
                if (endMarker) { map.removeLayer(endMarker); endMarker = null; }
                updateDrawUi();
            }
        }

        function removeRouteGeometry() {
            pathPoints = [];
            polyline.setLatLngs([]);
            if (carMarker) { map.removeLayer(carMarker); carMarker = null; }
            carPoint = null;
            if (decorator) { map.removeLayer(decorator); decorator = null; }
            var summary = document.getElementById('routeSummary');
            summary.style.display = 'none';
            summary.textContent = '';
            document.getElementById('btnStart').disabled = true;
            updateRouteFileActions();
        }

        function endpointIcon(role) {
            var isStart = role === 'start';
            var color = isStart ? '#27ae60' : '#ef5350';
            var text = isStart ? '起' : '终';
            return L.divIcon({ className:'custom-div-icon', html:""<div style='background:"" + color + "";width:28px;height:28px;border-radius:50%;text-align:center;color:white;font-size:12px;font-weight:bold;line-height:28px;border:2px solid white;box-shadow:0 3px 10px rgba(0,0,0,.35);'>"" + text + ""</div>"", iconSize:[32,32], iconAnchor:[16,16] });
        }

        function chooseNavigationPoint(role, lat, lng, name) {
            if (isSimulating) return;
            setRouteMode('navigation');
            setRoutePoint(role, lat, lng, name);
            if (routeStart && routeEnd) planNavigationRoute();
        }

        function addDrawPoint(lat, lng) {
            if (isSimulating || routeMode !== 'draw') return;
            pathPoints.push({lat:lat, lng:lng});
            renderDrawPath();
        }

        function undoDrawPoint() {
            if (isSimulating || routeMode !== 'draw' || !pathPoints.length) return;
            pathPoints.pop();
            renderDrawPath();
        }

        function renderDrawPath() {
            renderPathPolyline();
            if (decorator) { map.removeLayer(decorator); decorator = null; }
            refreshDrawMarkers();
            drawPathDecorations();
            updateDrawUi();
        }

        function refreshDrawMarkers() {
            if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
            if (endMarker) { map.removeLayer(endMarker); endMarker = null; }
            if (!pathPoints.length) return;
            var first = pathPoints[0];
            var firstDisplay = logicalToDisplayPoint(first);
            startMarker = L.marker([firstDisplay.lat,firstDisplay.lng], {icon:endpointIcon('start')}).addTo(map).bindTooltip('绘制起点');
            if (pathPoints.length > 1) {
                var last = pathPoints[pathPoints.length - 1];
                var lastDisplay = logicalToDisplayPoint(last);
                endMarker = L.marker([lastDisplay.lat,lastDisplay.lng], {icon:endpointIcon('end')}).addTo(map).bindTooltip('绘制终点');
            }
        }

        function updateDrawUi() {
            var count = pathPoints.length;
            document.getElementById('drawPointCount').textContent = count + ' 个点';
            document.getElementById('drawState').textContent = count === 0 ? '等待绘制' : (count === 1 ? '请继续选点' : '路线已就绪');
            document.getElementById('btnUndoDraw').disabled = count === 0 || isSimulating;
            document.getElementById('btnStart').disabled = count < 2 || isSimulating;
            updateRouteFileActions();
        }

        function createNavigationMarker(role, point) {
            if (!point) return null;
            var display = logicalToDisplayPoint(point);
            var routeMarker = L.marker([display.lat,display.lng], {icon:endpointIcon(role), draggable:true}).addTo(map).bindTooltip((role === 'start' ? '起点：' : '终点：') + point.name);
            routeMarker.on('dragend', function(e) {
                var displayPoint = e.target.getLatLng();
                var logicalPoint = displayToLogicalPoint(displayPoint.lat,displayPoint.lng);
                setRoutePoint(role,logicalPoint.lat,logicalPoint.lng,'拖动选点');
            });
            return routeMarker;
        }

        function refreshNavigationMarkers() {
            if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
            if (endMarker) { map.removeLayer(endMarker); endMarker = null; }
            if (routeStart) startMarker = createNavigationMarker('start',routeStart);
            if (routeEnd) endMarker = createNavigationMarker('end',routeEnd);
        }

        function setRoutePoint(role, lat, lng, name) {
            if (isSimulating) return;
            removeRouteGeometry();
            var point = {lat:lat, lng:lng, name:name || '地图选点'};
            if (role === 'start') {
                routeStart = point;
                if (startMarker) map.removeLayer(startMarker);
                startMarker = createNavigationMarker('start',point);
            } else {
                routeEnd = point;
                if (endMarker) map.removeLayer(endMarker);
                endMarker = createNavigationMarker('end',point);
            }
            updateRouteUi();
        }

        function clearRoutePoint(role, suppressUpdate) {
            if (isSimulating) return;
            if (role === 'start') {
                routeStart = null;
                if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
            } else {
                routeEnd = null;
                if (endMarker) { map.removeLayer(endMarker); endMarker = null; }
            }
            removeRouteGeometry();
            if (!suppressUpdate) updateRouteUi();
        }

        function updateRouteUi() {
            document.getElementById('startPointName').textContent = routeStart ? routeStart.name : '尚未设置';
            document.getElementById('endPointName').textContent = routeEnd ? routeEnd.name : '尚未设置';
            document.getElementById('btnPlan').disabled = !(routeStart && routeEnd) || isSimulating;
            document.getElementById('routeState').textContent = routeStart && routeEnd ? '可以规划' : (routeStart ? '请选择终点' : '请选择起点');
        }

        function swapRoutePoints() {
            if (isSimulating || !routeStart || !routeEnd) return;
            var oldStart = routeStart, oldEnd = routeEnd;
            if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
            if (endMarker) { map.removeLayer(endMarker); endMarker = null; }
            routeStart = null; routeEnd = null;
            setRoutePoint('start', oldEnd.lat, oldEnd.lng, oldEnd.name);
            setRoutePoint('end', oldStart.lat, oldStart.lng, oldStart.name);
            planNavigationRoute();
        }

        function drawPathDecorations() {
            if (pathPoints.length >= 2 && !decorator && L.polylineDecorator) {
                decorator = L.polylineDecorator(polyline, { patterns:[{offset:25,repeat:100,symbol:L.Symbol.arrowHead({pixelSize:15,pathOptions:{fillOpacity:1,weight:0,className:'path-arrow',pane:'arrowPane'}})}] }).addTo(map);
            }
        }

        async function planNavigationRoute() {
            if (isSimulating || !routeStart || !routeEnd) return;
            if (routeController) routeController.abort();
            var currentRouteController = new AbortController();
            routeController = currentRouteController;
            var timeout = setTimeout(function(){ currentRouteController.abort(); }, 15000);
            var planButton = document.getElementById('btnPlan');
            var summary = document.getElementById('routeSummary');
            planButton.disabled = true;
            planButton.textContent = '正在规划…';
            document.getElementById('routeState').textContent = '规划中';
            summary.style.display = 'block';
            summary.textContent = '正在连接导航服务并计算道路路线…';
            try {
                var startWgs = convertCoordinate(routeStart.lat,routeStart.lng,coordinateSystem,'wgs84');
                var endWgs = convertCoordinate(routeEnd.lat,routeEnd.lng,coordinateSystem,'wgs84');
                var url = 'https://router.project-osrm.org/route/v1/driving/' + startWgs.lng + ',' + startWgs.lat + ';' + endWgs.lng + ',' + endWgs.lat + '?overview=simplified&geometries=geojson&steps=false';
                var response = await fetch(url,{signal:currentRouteController.signal});
                if (!response.ok) throw new Error('导航服务返回 HTTP ' + response.status);
                var data = await response.json();
                if (data.code !== 'Ok' || !data.routes || !data.routes.length) throw new Error('未找到可通行的驾车路线');
                var route = data.routes[0];
                pathPoints = route.geometry.coordinates.map(function(p){ return convertCoordinate(p[1],p[0],'wgs84',coordinateSystem); });
                if (pathPoints.length < 2) throw new Error('导航结果缺少有效轨迹点');
                renderPathPolyline();
                drawPathDecorations();
                map.fitBounds(polyline.getBounds(),{padding:[48,48],maxZoom:16});
                var distanceText = route.distance >= 1000 ? (route.distance/1000).toFixed(1) + ' km' : Math.round(route.distance) + ' m';
                var minutes = Math.max(1,Math.round(route.duration/60));
                summary.innerHTML = '<b>路线已生成</b>　' + distanceText + '　预计 ' + minutes + ' 分钟　' + pathPoints.length + ' 个轨迹点';
                document.getElementById('routeState').textContent = '已规划';
                document.getElementById('btnStart').disabled = false;
                updateRouteFileActions();
                postMapLog('已生成导航路线：' + distanceText + '，约 ' + minutes + ' 分钟');
            } catch (error) {
                removeRouteGeometry();
                summary.style.display = 'block';
                summary.textContent = error.name === 'AbortError' ? '路线规划超时，请稍后重试。' : '路线规划失败：' + error.message;
                document.getElementById('routeState').textContent = '规划失败';
            } finally {
                clearTimeout(timeout);
                if (routeController === currentRouteController) routeController = null;
                planButton.textContent = '重新规划路线';
                planButton.disabled = !(routeStart && routeEnd) || isSimulating;
            }
        }

        function startSimulation() {
            if (pathPoints.length < 2) {
                alert(routeMode === 'navigation' ? '请先设置起点和终点，并完成导航路线规划。' : '请在地图上至少绘制两个轨迹点。');
                return;
            }
            
            drawPathDecorations();
            
            isSimulating = true;
            document.getElementById('btnClear').disabled = true;
            document.getElementById('btnPlan').disabled = true;
            document.getElementById('coordinateSystem').disabled = true;
            document.getElementById('modeDraw').disabled = true;
            document.getElementById('modeNavigation').disabled = true;
            document.getElementById('btnUndoDraw').disabled = true;
            document.getElementById('btnStart').style.display = 'none';
            document.getElementById('btnStop').style.display = 'block';
            document.getElementById(routeMode === 'navigation' ? 'routeState' : 'drawState').textContent = '模拟中';
            updateRouteFileActions();
            
            var speed = parseFloat(document.getElementById('simSpeed').value) || 60.5;
            
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'simulate_path', path: pathPoints, speed: speed, coordinateSystem: coordinateSystem }));
            }
        }

        function stopSimulation() {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'stop_simulation' }));
            }
            simulationFinished();
        }

        function simulationFinished() {
            isSimulating = false;
            document.getElementById('btnClear').disabled = false;
            document.getElementById('btnPlan').disabled = !(routeStart && routeEnd);
            document.getElementById('coordinateSystem').disabled = false;
            document.getElementById('modeDraw').disabled = false;
            document.getElementById('modeNavigation').disabled = false;
            document.getElementById('btnStart').style.display = 'block';
            document.getElementById('btnStart').disabled = pathPoints.length < 2;
            document.getElementById('btnStop').style.display = 'none';
            if (routeMode === 'navigation') {
                document.getElementById('routeState').textContent = pathPoints.length >= 2 ? '已规划' : '等待规划';
            } else {
                updateDrawUi();
            }
            updateRouteFileActions();
        }

        function changeSpeed() {
            if (isSimulating) {
                var speed = parseFloat(document.getElementById('simSpeed').value) || 60.5;
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(JSON.stringify({ type: 'simulate_speed', speed: speed }));
                }
            }
        }

        function updateCarLocation(lat, lng, direction) {
            if (!carMarker) {
                var arrowIcon = L.divIcon({
                    className: 'car-arrow',
                    html: '<div id=""carArrowSvg"" style=""width:100%;height:100%;transform-origin:center;transform:rotate(0deg);""><svg viewBox=""0 0 24 24"" style=""width:32px; height:32px;""><path class=""car-arrow-svg"" d=""M12 2L2 22l10-5 10 5L12 2z""/></svg></div>',
                    iconSize: [32, 32],
                    iconAnchor: [16, 16]
                });
                carMarker = L.marker([lat, lng], {
                    icon: arrowIcon,
                    zIndexOffset: 1000
                }).addTo(map);
            } else {
                carMarker.setLatLng([lat, lng]);
            }
            if (direction !== undefined) {
                var svgEl = document.getElementById('carArrowSvg');
                if (svgEl) {
                    svgEl.style.transform = 'rotate(' + direction + 'deg)';
                }
            }
            if (!map.getBounds().contains([lat, lng])) {
                map.panTo([lat, lng]);
            }
        }

        function updateCarLocationLogical(lat, lng, direction) {
            carPoint = {lat:Number(lat),lng:Number(lng)};
            carDirection = Number(direction) || 0;
            var display = logicalToDisplayPoint({lat:lat,lng:lng});
            updateCarLocation(display.lat, display.lng, direction);
        }

        function applyCoords(lat, lng) {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'apply', lat: lat, lng: lng, coordinateSystem: coordinateSystem }));
            }
        }

        function updateSearchHint() {
            var query = document.getElementById('searchInput').value.trim();
            if (query.length < 2) {
                document.getElementById('searchResults').style.display = 'none';
                document.getElementById('searchStatus').textContent = query.length ? '继续输入' : '';
            } else {
                document.getElementById('searchStatus').textContent = '按 Enter 或点击搜索';
            }
        }

        function escapeHtml(value) {
            return String(value || '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;').replace(/'/g,'&#39;');
        }

        function resultTitle(item) {
            return item.name || (item.namedetails && (item.namedetails['name:zh'] || item.namedetails.name)) || (item.display_name || '未命名地点').split(',')[0];
        }

        async function search() {
            var query = document.getElementById('searchInput').value.trim();
            if (query.length < 2) return;
            if (searchController) searchController.abort();
            var currentSearchController = new AbortController();
            searchController = currentSearchController;
            var status = document.getElementById('searchStatus');
            var resultBox = document.getElementById('searchResults');
            status.textContent = '搜索中…';
            try {
                var bounds = map.getBounds();
                var southWest = convertCoordinate(bounds.getSouth(),bounds.getWest(),'gcj02','wgs84');
                var northEast = convertCoordinate(bounds.getNorth(),bounds.getEast(),'gcj02','wgs84');
                var viewBox = southWest.lng + ',' + northEast.lat + ',' + northEast.lng + ',' + southWest.lat;
                var url = 'https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&namedetails=1&limit=8&accept-language=zh-CN,zh,en&bounded=0&viewbox=' + encodeURIComponent(viewBox) + '&q=' + encodeURIComponent(query);
                var response = await fetch(url,{signal:currentSearchController.signal});
                if (!response.ok) throw new Error('搜索服务返回 HTTP ' + response.status);
                searchResults = await response.json();
                renderSearchResults();
                status.textContent = searchResults.length ? searchResults.length + ' 个结果' : '没有找到';
            } catch (error) {
                if (error.name === 'AbortError') return;
                searchResults = [];
                resultBox.style.display = 'block';
                resultBox.innerHTML = '<div class=""search-result""><div class=""result-address"">搜索失败：' + escapeHtml(error.message) + '</div></div>';
                status.textContent = '搜索失败';
            } finally {
                if (searchController === currentSearchController) searchController = null;
            }
        }

        function renderSearchResults() {
            var box = document.getElementById('searchResults');
            if (!searchResults.length) {
                box.style.display = 'block';
                box.innerHTML = '<div class=""search-result""><div class=""result-address"">没有匹配地点，请尝试加入城市、区县或道路名称。</div></div>';
                return;
            }
            box.innerHTML = searchResults.map(function(item,index){
                var title = escapeHtml(resultTitle(item));
                var address = escapeHtml(item.display_name || '');
                var type = escapeHtml((item.type || item.addresstype || '地点').replace(/_/g,' '));
                var logical = convertCoordinate(Number(item.lat),Number(item.lon),'wgs84',coordinateSystem);
                return '<div class=""search-result"" onclick=""selectSearchResult(' + index + ')""><div class=""result-title"">' + title + '</div><div class=""result-address"">' + address + '</div><div class=""result-meta"">类型：' + type + '　' + coordinateSystemLabel(coordinateSystem) + '：' + logical.lat.toFixed(6) + ', ' + logical.lng.toFixed(6) + '</div><div class=""result-actions""><button class=""secondary-button"" onclick=""event.stopPropagation();useSearchResult(' + index + ',&#39;start&#39;)"">设为起点</button><button class=""secondary-button"" onclick=""event.stopPropagation();useSearchResult(' + index + ',&#39;end&#39;)"">设为终点</button><button class=""primary-button"" onclick=""event.stopPropagation();selectSearchResult(' + index + ')"">查看</button></div></div>';
            }).join('');
            box.style.display = 'block';
        }

        function searchResultLogicalPoint(index) {
            var item = searchResults[index];
            if (!item) return null;
            return convertCoordinate(parseFloat(item.lat),parseFloat(item.lon),'wgs84',coordinateSystem);
        }

        function selectSearchResult(index) {
            var point = searchResultLogicalPoint(index);
            if (!point) return;
            var display = logicalToDisplayPoint(point);
            map.setView([display.lat,display.lng],16);
            placeMarker(point.lat,point.lng);
        }

        function useSearchResult(index, role) {
            var item = searchResults[index];
            var point = searchResultLogicalPoint(index);
            if (!item || !point) return;
            setRouteMode('navigation');
            setRoutePoint(role,point.lat,point.lng,resultTitle(item));
            var display = logicalToDisplayPoint(point);
            map.setView([display.lat,display.lng],15);
            if (routeStart && routeEnd) planNavigationRoute();
        }

        function showNetworkLocation(location) {
            if (!location) return;
            var display = convertCoordinate(location.lat,location.lng,'wgs84','gcj02');
            if (networkLocationMarker) map.removeLayer(networkLocationMarker);
            if (networkLocationAccuracyCircle) map.removeLayer(networkLocationAccuracyCircle);
            networkLocationAccuracyCircle = L.circle([display.lat,display.lng], {
                radius: Math.max(1000, location.accuracy || 25000),
                color:'#2196f3', weight:1, opacity:.42, fillColor:'#42a5f5', fillOpacity:.07,
                interactive:false
            }).addTo(map);
            networkLocationMarker = L.circleMarker([display.lat,display.lng], {
                radius:8, color:'#ffffff', weight:3, fillColor:'#2196f3', fillOpacity:1
            }).addTo(map).bindTooltip('设备网络位置（城市级）' + (location.label ? '<br>' + escapeHtml(location.label) : ''));
        }

        function locateDevicePosition(showFailure) {
            var locateButton = document.getElementById('locateButton');
            if (locateButton) locateButton.disabled = true;
            networkLocationRequestedManually = !!showFailure;
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({type:'request_network_location',showFailure:!!showFailure}));
            } else {
                networkLocationFailed('当前环境不支持网络定位');
            }
        }

        function applyNetworkLocation(location) {
            var locateButton = document.getElementById('locateButton');
            if (locateButton) {
                locateButton.disabled = false;
                locateButton.textContent = '◎';
                locateButton.title = '重新定位到设备网络位置';
            }
            if (!location || !Number.isFinite(Number(location.lat)) || !Number.isFinite(Number(location.lng))) {
                networkLocationFailed('网络定位结果无效');
                return;
            }
            networkLocationWgs = {
                lat:Number(location.lat),
                lng:Number(location.lng),
                accuracy:Math.max(5000,Number(location.accuracy || 0) || 25000),
                label:location.label || ''
            };
            if (mapDefaultLocation === 'auto' || networkLocationRequestedManually) {
                showNetworkLocation(networkLocationWgs);
                var display = convertCoordinate(networkLocationWgs.lat,networkLocationWgs.lng,'wgs84','gcj02');
                map.setView([display.lat,display.lng],11);
            }
            networkLocationRequestedManually = false;
            postMapLog('地图已定位到设备网络位置' + (networkLocationWgs.label ? '：' + networkLocationWgs.label : ''));
        }

        function networkLocationFailed(message) {
            var locateButton = document.getElementById('locateButton');
            if (locateButton) {
                locateButton.disabled = false;
                locateButton.textContent = '!';
                locateButton.title = '网络定位暂不可用：' + (message || '未知错误') + '。点击重试';
            }
            if (mapDefaultLocation === 'auto' || networkLocationRequestedManually) map.setView([35.8617,104.1954],4);
            networkLocationRequestedManually = false;
            postMapLog('设备网络定位不可用，已显示全国范围：' + (message || '未知错误'));
        }

        function postMapLog(message) {
            if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(JSON.stringify({type:'map_log',message:message}));
        }

        document.addEventListener('keydown',function(event){ if(event.key === 'Escape') { closeMapSettings(); cancelCoordinateSystemChange(); } });
        setRouteMode('navigation');
        requestMapSettings();
    </script>
</body>
</html>";
            MapWebView.WebMessageReceived -= MapWebView_WebMessageReceived;
            MapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
            MapWebView.NavigateToString(mapHtml);
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
                        vm.LogMessage("地图", "地图选点设置已保存");
                    }
                    else if (type == "save_map_route")
                    {
                        await SaveMapRouteAsync(data["route"]);
                    }
                    else if (type == "open_map_route")
                    {
                        await OpenMapRouteAsync();
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
                    else if (type == "tencent_search")
                    {
                        var requestId = data["requestId"]?.ToString() ?? string.Empty;
                        var query = data["query"]?.ToString() ?? string.Empty;
                        if (!TryReadMapCoordinate(data, "lat", "lng", out var latitude, out var longitude))
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
                            vm.StartPathSimulation(geoPoints);
                        }
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
            _ => throw new System.IO.InvalidDataException("路线模式无效")
        };
        document.CoordinateSystem = document.CoordinateSystem?.Trim().ToLowerInvariant() switch
        {
            "wgs84" => "wgs84",
            "gcj02" => "gcj02",
            "bd09" => "bd09",
            _ => throw new System.IO.InvalidDataException("路线坐标系无效")
        };
        if (document.Points is null || document.Points.Count < 2)
        {
            throw new System.IO.InvalidDataException("路线至少需要两个轨迹点");
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
                    request.Headers.UserAgent.ParseAdd("CarTerminalSimulation/Preview9");
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
            vm.IsSettingsOpen = true;
        }
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
                        _settingsWindow = new ThemeSettingsWindow
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
