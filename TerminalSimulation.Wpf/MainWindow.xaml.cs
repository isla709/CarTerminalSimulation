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

namespace TerminalSimulation.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _videoLayoutTimer;
    private readonly HashSet<LibVLCSharp.WPF.VideoView> _loadedVideoViews = new();

    public MainWindow()
    {
        InitializeComponent();
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
        body { margin: 0; padding: 0; font-family: 'Microsoft YaHei', sans-serif; background: #222; }
        #map { width: 100vw; height: 100vh; transition: filter 0.5s ease; }
        #top-panel { 
            position: absolute; top: 15px; left: 50px; z-index: 1000; 
            background: white; padding: 10px; border-radius: 8px; 
            box-shadow: 0 2px 10px rgba(0,0,0,0.2);
            display: flex; flex-direction: column; gap: 8px;
        }
        /* Map Styles */
        .map-style-default { filter: none; }
        .map-style-dark { filter: invert(100%) hue-rotate(180deg) brightness(95%) contrast(90%); }
        .map-style-navy { filter: invert(100%) hue-rotate(200deg) sepia(20%) brightness(110%) contrast(90%); }
        .map-style-emerald { filter: invert(100%) hue-rotate(260deg) sepia(40%) saturate(150%) brightness(95%) contrast(90%); }
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

        .car-arrow { transition: transform 0.1s linear; }
        
        .row { display: flex; gap: 8px; align-items: center; }
        input { padding: 6px 12px; border: 1px solid #ccc; border-radius: 4px; outline: none; width: 150px; }
        button { padding: 6px 12px; background: #2196F3; color: white; border: none; border-radius: 4px; cursor: pointer; font-size:13px; }
        button:hover { background: #1976D2; }
        button:disabled { background: #cccccc; cursor: not-allowed; }
        .danger { background: #F44336; }
        .danger:hover { background: #D32F2F; }
        .success { background: #4CAF50; }
        .success:hover { background: #388E3C; }
        label { font-size: 14px; font-weight: bold; }
    </style>
</head>
<body class=""theme-neon-blue"">
    <div id='top-panel'>
        <div class='row'>
            <input type='text' id='searchInput' placeholder='搜索地点...' onkeypress='if(event.key===""Enter"") search()'/>
            <button onclick='search()'>搜索</button>
            <span style=""margin-left: 10px;""></span>
            <label style=""display:flex; align-items:center; gap:4px; font-weight:normal; font-size:13px;"">
                风格:
                <select id=""mapStyle"" onchange=""changeMapStyle()"" style=""padding:4px; border-radius:4px; outline:none;"">
                    <option value=""map-style-default"">默认高德</option>
                    <option value=""map-style-dark"">幻影黑</option>
                    <option value=""map-style-navy"">极夜蓝</option>
                    <option value=""map-style-emerald"">墨绿雷达</option>
                    <option value=""map-style-grayscale"">黑白灰</option>
                </select>
            </label>
            <span style=""margin-left: 10px;""></span>
            <label id=""pathStyleContainer"" style=""display:none; align-items:center; gap:4px; font-weight:normal; font-size:13px;"">
                路线:
                <select id=""pathStyle"" onchange=""changePathStyle()"" style=""padding:4px; border-radius:4px; outline:none;"">
                    <option value=""theme-neon-blue"">霓虹青蓝</option>
                    <option value=""theme-neon-purple"">赛博紫粉</option>
                    <option value=""theme-solid-red"">绿线红车(无光)</option>
                    <option value=""theme-solid-orange"">蓝线橙车(无光)</option>
                </select>
            </label>
            <span style=""margin-left: 10px;""></span>
            <label style=""display:flex; align-items:center; gap:4px; font-weight:normal;"">
                <input type=""checkbox"" id=""pathModeToggle"" onchange=""togglePathMode()"" style=""width:auto; margin:0;""/> 路径模拟
            </label>
        </div>
        <div class='row' id='pathControls' style='display: none;'>
            <label style=""display:flex; align-items:center; gap:4px; font-weight:normal; font-size:13px;"">
                速度: <input type='number' id='simSpeed' value='60.5' step='0.1' style='width:60px; padding:4px;' onchange='changeSpeed()'/>
            </label>
            <button id='btnClear' class='danger' onclick='clearPath()'>清除路径</button>
            <button id='btnStart' class='success' onclick='startSimulation()'>应用并开始</button>
            <button id='btnStop' class='danger' onclick='stopSimulation()' style='display:none;'>中断模拟</button>
        </div>
    </div>
    <div id='map'></div>
    <script>
        var map = L.map('map').setView([39.9042, 116.4074], 11);
        L.tileLayer('https://webrd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=8&x={x}&y={y}&z={z}', {
            subdomains: ['1', '2', '3', '4'],
            attribution: '© 高德地图'
        }).addTo(map);

        var marker;
        var carMarker;
        var pathMode = false;
        var isSimulating = false;
        var pathPoints = [];
        var polyline = L.polyline([], {weight: 4, className: 'path-line'}).addTo(map);
        var tempMarker;
        var startMarker, endMarker;
        var decorator;

        map.createPane('arrowPane');
        map.getPane('arrowPane').style.zIndex = 450;
        map.getPane('arrowPane').style.pointerEvents = 'none';

        function changeMapStyle() {
            var mapEl = document.getElementById('map');
            var styleClass = document.getElementById('mapStyle').value;
            mapEl.className = styleClass;
        }

        function changePathStyle() {
            var bodyEl = document.body;
            bodyEl.classList.remove('theme-neon-blue', 'theme-neon-purple', 'theme-solid-red', 'theme-solid-orange');
            var themeClass = document.getElementById('pathStyle').value;
            bodyEl.classList.add(themeClass);
        }

        function togglePathMode() {
            pathMode = document.getElementById('pathModeToggle').checked;
            document.getElementById('pathControls').style.display = pathMode ? 'flex' : 'none';
            document.getElementById('pathStyleContainer').style.display = pathMode ? 'flex' : 'none';
        }

        map.on('click', function(e) {
            if (isSimulating) return; // ignore clicks during simulation
            
            var lat = e.latlng.lat;
            var lng = e.latlng.lng;
            
            if (pathMode) {
                pathPoints.push({lat: lat, lng: lng});
                polyline.addLatLng(e.latlng);
                
                if (pathPoints.length === 1) {
                    tempMarker = L.circleMarker([lat, lng], {color: 'blue', radius: 5}).addTo(map);
                } else if (tempMarker) {
                    map.removeLayer(tempMarker);
                    tempMarker = null;
                }
            } else {
                placeMarker(lat, lng);
            }
        });

        function placeMarker(lat, lng) {
            if (marker) map.removeLayer(marker);
            marker = L.marker([lat, lng]).addTo(map)
                .bindPopup('<div style=""text-align:center;""><b>当前坐标</b><br>纬度: ' + lat.toFixed(6) + '<br>经度: ' + lng.toFixed(6) + 
                           '<br><br><button style=""margin-top:5px;"" onclick=""applyCoords(' + lat + ',' + lng + ')"">应用坐标到位置汇报</button></div>')
                .openPopup();
        }

        function clearPath() {
            if (isSimulating) return;
            pathPoints = [];
            polyline.setLatLngs([]);
            if (tempMarker) { map.removeLayer(tempMarker); tempMarker = null; }
            if (carMarker) { map.removeLayer(carMarker); carMarker = null; }
            if (startMarker) { map.removeLayer(startMarker); startMarker = null; }
            if (endMarker) { map.removeLayer(endMarker); endMarker = null; }
            if (decorator) { map.removeLayer(decorator); decorator = null; }
        }

        function drawPathDecorations() {
            if (!startMarker && pathPoints.length >= 2) {
                var start = pathPoints[0];
                var end = pathPoints[pathPoints.length - 1];

                var startIcon = L.divIcon({ className: 'custom-div-icon', html: ""<div style='background-color:#4CAF50; width:24px; height:24px; border-radius:50%; text-align:center; color:white; font-size:12px; font-weight:bold; line-height:24px; border:2px solid white; box-shadow:0 0 5px rgba(0,0,0,0.5);'>起</div>"", iconSize: [28, 28], iconAnchor: [14, 14] });
                var endIcon = L.divIcon({ className: 'custom-div-icon', html: ""<div style='background-color:#F44336; width:24px; height:24px; border-radius:50%; text-align:center; color:white; font-size:12px; font-weight:bold; line-height:24px; border:2px solid white; box-shadow:0 0 5px rgba(0,0,0,0.5);'>终</div>"", iconSize: [28, 28], iconAnchor: [14, 14] });

                startMarker = L.marker([start.lat, start.lng], {icon: startIcon}).addTo(map).bindPopup('起点');
                endMarker = L.marker([end.lat, end.lng], {icon: endIcon}).addTo(map).bindPopup('终点');

                if (L.polylineDecorator) {
                    decorator = L.polylineDecorator(polyline, {
                        patterns: [
                            {offset: 25, repeat: 100, symbol: L.Symbol.arrowHead({pixelSize: 15, pathOptions: {fillOpacity: 1, weight: 0, className: 'path-arrow', pane: 'arrowPane'}})}
                        ]
                    }).addTo(map);
                }
            }
        }

        function startSimulation() {
            if (pathPoints.length < 2) {
                alert('请至少在地图上点击绘制2个点形成路径！');
                return;
            }
            
            drawPathDecorations();
            
            isSimulating = true;
            document.getElementById('btnClear').disabled = true;
            document.getElementById('btnStart').style.display = 'none';
            document.getElementById('btnStop').style.display = 'inline-block';
            
            var speed = parseFloat(document.getElementById('simSpeed').value) || 60.5;
            
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'simulate_path', path: pathPoints, speed: speed }));
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
            document.getElementById('btnStart').style.display = 'inline-block';
            document.getElementById('btnStop').style.display = 'none';
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

        function applyCoords(lat, lng) {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify({ type: 'apply', lat: lat, lng: lng }));
            }
        }

        function search() {
            var query = document.getElementById('searchInput').value;
            if(!query) return;
            fetch('https://nominatim.openstreetmap.org/search?format=json&q=' + encodeURIComponent(query))
                .then(res => res.json())
                .then(data => {
                    if (data && data.length > 0) {
                        var lat = parseFloat(data[0].lat);
                        var lon = parseFloat(data[0].lon);
                        map.setView([lat, lon], 14);
                        if (!pathMode) {
                            placeMarker(lat, lon);
                        }
                    } else {
                        alert('未找到该地点');
                    }
                })
                .catch(err => alert('搜索出错: ' + err));
        }
    </script>
</body>
</html>";
            MapWebView.NavigateToString(mapHtml);
            MapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
        }
        catch
        {
            _mapInitialized = false;
            // Fallback if WebView2 runtime is missing
            System.Diagnostics.Debug.WriteLine("WebView2 init failed.");
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep both work areas usable while allowing compact laptop windows.
        ControlColumn.Width = new GridLength(ResponsiveLayoutPolicy.GetMainControlColumnWidth(ActualWidth));
        RequestVideoViewsUpdate();
    }

    private void MapWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
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
                    if (type == "apply")
                    {
                        if (data["lat"] != null && double.TryParse(data["lat"]!.ToString(), out double lat))
                        {
                            vm.Latitude = Math.Round(lat, 6);
                        }
                        if (data["lng"] != null && double.TryParse(data["lng"]!.ToString(), out double lng))
                        {
                            vm.Longitude = Math.Round(lng, 6);
                        }
                    }
                    else if (type == "simulate_path")
                    {
                        var pathArray = data["path"]?.AsArray();
                        if (pathArray != null)
                        {
                            if (data["speed"] != null && double.TryParse(data["speed"]!.ToString(), out double speed))
                            {
                                vm.Speed = speed;
                            }

                            var geoPoints = new System.Collections.Generic.List<TerminalSimulation.Wpf.ViewModels.GeoPoint>();
                            foreach (var p in pathArray)
                            {
                                if (p != null)
                                {
                                    geoPoints.Add(new TerminalSimulation.Wpf.ViewModels.GeoPoint
                                    {
                                        Lat = (double)p["lat"]!,
                                        Lng = (double)p["lng"]!
                                    });
                                }
                            }
                            // Attach Action so VM can update car position on map
                            vm.OnMapCarMoved = (lat, lng) =>
                            {
                                MapWebView.CoreWebView2.ExecuteScriptAsync($"updateCarLocation({lat}, {lng}, {vm.Direction})");
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
            if (MainTabControl.SelectedIndex == 1)
            {
                _ = InitializeMapAsync();
            }
            RequestVideoViewsUpdate();
        }
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
