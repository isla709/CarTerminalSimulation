using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SukiUI.Controls;
using TerminalSimulation.PluginBase.Avalonia;
using TerminalSimulation.Avalonia;
using TerminalSimulation.Avalonia.ViewModels;

namespace TerminalSimulation.Avalonia;

/// <summary>
/// Interaction logic for MainWindow.xaml — SukiUI SukiWindow provides native title bar.
/// </summary>
public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // TODO: Phase 4 — Reimplement map with Avalonia.WebView once available
        // InitializeMapAsync();
        this.Loaded += MainWindow_Loaded;
    }

    // TODO: Phase 4 — Reimplement map initialization with Avalonia.WebView
    /*
    private async void InitializeMapAsync()
    {
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
        .map-style-default { filter: none; }
        .map-style-dark { filter: invert(100%) hue-rotate(180deg) brightness(95%) contrast(90%); }
        .map-style-navy { filter: invert(100%) hue-rotate(200deg) sepia(20%) brightness(110%) contrast(90%); }
        .map-style-emerald { filter: invert(100%) hue-rotate(260deg) sepia(40%) saturate(150%) brightness(95%) contrast(90%); }
        .map-style-emerald { filter: invert(100%) hue-rotate(260deg) sepia(40%) saturate(150%) brightness(95%) contrast(90%); }
        .map-style-grayscale { filter: grayscale(100%) brightness(110%) contrast(90%); }

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
            <input type='text' id='searchInput' placeholder='搜索地点...' onkeypress='if(event.key==""Enter"") search()'/>
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
            if (isSimulating) return;

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
            System.Diagnostics.Debug.WriteLine("WebView2 init failed.");
        }
    }
    */

    // TODO: Phase 4 — Reimplement map message handling with Avalonia.WebView
    /*
    private void MapWebView_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (!string.IsNullOrEmpty(json))
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json);
                if (data != null && DataContext is MainViewModel vm)
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

                            var geoPoints = new System.Collections.Generic.List<GeoPoint>();
                            foreach (var p in pathArray)
                            {
                                if (p != null)
                                {
                                    geoPoints.Add(new GeoPoint
                                    {
                                        Lat = (double)p["lat"]!,
                                        Lng = (double)p["lng"]!
                                    });
                                }
                            }
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
        catch { }
    }
    */

    private void LogMessages_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm != null && LogListBox.Items.Count > 0)
        {
            var scrollViewer = LogListBox.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (scrollViewer != null)
                scrollViewer.ScrollToEnd();
        }
    }

    // TitleBar_PointerPressed, BtnMinimize_Click, BtnMaximize_Click, BtnClose_Click removed —
    // SukiWindow provides native title bar with built-in drag, min/max/close handling.

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsSettingsOpen = true;
        }
    }

    private ThemeSettingsWindow? _settingsWindow;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.LogMessages.CollectionChanged += LogMessages_CollectionChanged;
            vm.PropertyChanged += Vm_PropertyChanged;
        }

        try
        {
            var configJson = System.IO.File.ReadAllText("terminal_config.json");
            var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(configJson);
        }
        catch { }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSettingsOpen))
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsSettingsOpen)
                {
                    if (_settingsWindow == null)
                    {
                        _settingsWindow = new ThemeSettingsWindow
                        {
                            DataContext = vm,
                            Width = this.Width,
                            Height = this.Height,
                        };
                        _settingsWindow.Closed += (s, args) =>
                        {
                            _settingsWindow = null;
                            vm.IsSettingsOpen = false;
                        };
                        _settingsWindow.ShowDialog(this);
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

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is System.IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async void AnalyzerTreeView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            if (sender is TreeView tv && tv.SelectedItem is AnalyzerNode node)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync($"{node.Name} {node.Value}".Trim());
                }
                e.Handled = true;
            }
        }
    }
}
