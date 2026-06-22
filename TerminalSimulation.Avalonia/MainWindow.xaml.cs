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
using TerminalSimulation.Avalonia.ViewModels.Plugins;
using System.Linq;

namespace TerminalSimulation.Avalonia;

/// <summary>
/// Interaction logic for MainWindow.xaml — SukiUI SukiWindow provides native title bar.
/// </summary>
public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        InitializeMap();
        this.Loaded += MainWindow_Loaded;
    }

    private global::AvaloniaWebView.WebView _mapWebView;

    private void InitializeMap()
    {
        try
        {
            _mapWebView = new global::AvaloniaWebView.WebView();
            MapWebViewContainer.Child = _mapWebView;

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
            _mapWebView.HtmlContent = mapHtml;
            _mapWebView.WebMessageReceived += MapWebView_WebMessageReceived;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView init failed: {ex.Message}");
        }
    }

    private void MapWebView_WebMessageReceived(object? sender, global::WebViewCore.Events.WebViewMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.Message;
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
                                _mapWebView.ExecuteScriptAsync($"updateCarLocation({lat}, {lng}, {vm.Direction})");
                            };
                            vm.OnSimulationFinished = () =>
                            {
                                _mapWebView.ExecuteScriptAsync("simulationFinished()");
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
            vm.OpenedUtilityTabs.CollectionChanged += OpenedUtilityTabs_CollectionChanged;
        }

        try
        {
            var configJson = System.IO.File.ReadAllText("terminal_config.json");
            var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(configJson);
        }
        catch { }
    }

    private void OpenedUtilityTabs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (OpenedPluginTab tabData in e.NewItems)
            {
                var stackPanel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
                
                var iconKind = Material.Icons.MaterialIconKind.Toolbox;
                if (!string.IsNullOrEmpty(tabData.IconKind) && Enum.TryParse<Material.Icons.MaterialIconKind>(tabData.IconKind, true, out var parsedKind))
                {
                    iconKind = parsedKind;
                }

                var icon = new Material.Icons.Avalonia.MaterialIcon 
                { 
                    Kind = iconKind,
                    Width = 16, Height = 16,
                    Margin = new global::Avalonia.Thickness(0, 0, 8, 0),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                };
                
                var textBlock = new TextBlock 
                { 
                    Text = tabData.Title,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    FontWeight = global::Avalonia.Media.FontWeight.SemiBold
                };
                
                var closeButton = new Button 
                { 
                    Width = 20, Height = 20,
                    Margin = new global::Avalonia.Thickness(8, 0, -8, 0),
                    Padding = new global::Avalonia.Thickness(0),
                    Command = tabData.CloseCommand
                };
                closeButton.Classes.Add("Icon");
                closeButton.Content = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.Close, Width = 12, Height = 12 };
                
                stackPanel.Children.Add(icon);
                stackPanel.Children.Add(textBlock);
                stackPanel.Children.Add(closeButton);

                var tabItem = new TabItem
                {
                    Header = stackPanel,
                    Tag = tabData
                };
                tabItem.Classes.Add("BrowserTab");
                
                if (tabData.Content is Control control)
                {
                    var innerBorder = new Border { Child = control };
                    innerBorder.Classes.Add("TabContentInner");
                    
                    var outerGrid = new Grid { Margin = new global::Avalonia.Thickness(16) };
                    outerGrid.Children.Add(innerBorder);

                    var glassCard = new SukiUI.Controls.GlassCard
                    {
                        CornerRadius = new global::Avalonia.CornerRadius(0, 8, 8, 8),
                        Padding = new global::Avalonia.Thickness(0),
                        Content = outerGrid
                    };
                    glassCard.Classes.Add("TabContentInner");

                    var outerBorder = new Border { Child = glassCard };
                    outerBorder.Classes.Add("TabContentOuter");
                    
                    tabItem.Content = outerBorder;
                }

                UtilitiesTabControl.Items.Add(tabItem);
                UtilitiesTabControl.SelectedItem = tabItem;
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (OpenedPluginTab tabData in e.OldItems)
            {
                var tabItem = UtilitiesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == tabData);
                if (tabItem != null)
                {
                    UtilitiesTabControl.Items.Remove(tabItem);
                }
            }
        }
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

    private async void CopyAnalyzerTreeItem_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var tree = this.FindControl<TreeView>("AnalyzerTreeView");
        if (tree?.SelectedItem is ViewModels.AnalyzerNode node)
        {
            var text = string.IsNullOrWhiteSpace(node.Value) ? node.Name : $"{node.Name} {node.Value}";
            if (TopLevel.GetTopLevel(this)?.Clipboard != null)
            {
                await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(text);
            }
        }
    }

    private async void CopyAnalyzerTableItem_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("AnalyzerDataGrid");
        if (grid == null) return;

        string? text = null;
        if (grid.SelectedItem is ViewModels.AnalyzerTableRow row)
        {
            if (grid.CurrentColumn is global::Avalonia.Controls.DataGridTextColumn textCol)
            {
                var binding = textCol.Binding as global::Avalonia.Data.Binding;
                var path = binding?.Path;
                if (path == "Field") text = row.Field;
                else if (path == "HexData") text = row.HexData;
                else if (path == "DataType") text = row.DataType;
                else if (path == "OffsetStr") text = row.OffsetStr;
                else if (path == "LengthStr") text = row.LengthStr;
                else if (path == "Result") text = row.Result;
            }

            if (string.IsNullOrEmpty(text))
            {
                text = $"{row.Field}\t{row.HexData}\t{row.DataType}\t{row.OffsetStr}\t{row.LengthStr}\t{row.Result}";
            }
        }

        if (!string.IsNullOrEmpty(text) && TopLevel.GetTopLevel(this)?.Clipboard != null)
        {
            await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(text);
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;

    private void ClipHwndAndParents(IntPtr hwnd, int left, int top, int right, int bottom)
    {
        if (!OperatingSystem.IsWindows()) return;
        
        while (hwnd != IntPtr.Zero)
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            if ((style & WS_CHILD) == 0) break;

            IntPtr hRgn = CreateRectRgn(left, top, right, bottom);
            if (hRgn != IntPtr.Zero) SetWindowRgn(hwnd, hRgn, true);
            
            hwnd = GetParent(hwnd);
        }
    }

    private void VideoScrollViewerContainer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateVideoViewsClipping();
    }

    private void VideoScrollViewerContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateVideoViewsClipping();
    }

    private void UpdateVideoViewsClipping()
    {
        if (VideoScrollViewerContainer == null) return;

        double viewportWidth = VideoScrollViewerContainer.Viewport.Width;
        double viewportHeight = VideoScrollViewerContainer.Viewport.Height;
        var viewportRect = new Rect(0, 0, viewportWidth, viewportHeight);

        var videoViews = this.GetVisualDescendants().OfType<LibVLCSharp.Avalonia.VideoView>().ToList();
        double dpiScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

        foreach (var videoView in videoViews)
        {
            try
            {
                if (!videoView.IsVisible) continue;
                if (videoView.DataContext is not ViewModels.VideoChannelItem vm) continue;
                if (vm.MediaPlayer == null) continue;
                
                IntPtr hwnd = vm.MediaPlayer.Hwnd;
                if (hwnd == IntPtr.Zero) continue;

                var transform = videoView.TransformToVisual(VideoScrollViewerContainer);
                if (transform == null) continue;

                var bounds = new Rect(0, 0, videoView.Bounds.Width, videoView.Bounds.Height);
                var relativeRect = bounds.TransformToAABB(transform.Value);

                var intersection = viewportRect.Intersect(relativeRect);

                if (intersection.Width <= 0 || intersection.Height <= 0)
                {
                    ClipHwndAndParents(hwnd, 0, 0, 0, 0);
                }
                else
                {
                    double left = Math.Max(0, -relativeRect.TopLeft.X);
                    double top = Math.Max(0, -relativeRect.TopLeft.Y);
                    double right = left + intersection.Width;
                    double bottom = top + intersection.Height;

                    int physLeft = (int)Math.Round(left * dpiScale);
                    int physTop = (int)Math.Round(top * dpiScale);
                    int physRight = (int)Math.Round(right * dpiScale);
                    int physBottom = (int)Math.Round(bottom * dpiScale);

                    ClipHwndAndParents(hwnd, physLeft, physTop, physRight, physBottom);
                }
            }
            catch { }
        }
    }

    private void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl tc && tc.Name == "MainTabControl")
        {
            // If we switch away from the Video tab (Index 4), stop all local video previews
            // to destroy the native HWNDs and prevent airspace popup bugs when switching back.
            if (tc.SelectedIndex != 4)
            {
                if (DataContext is MainViewModel vm)
                {
                    foreach (var channel in vm.VideoChannels)
                    {
                        channel.SuspendPlayback();
                    }
                }
            }
            else
            {
                if (DataContext is MainViewModel vm)
                {
                    foreach (var channel in vm.VideoChannels)
                    {
                        channel.ResumePlayback();
                    }
                }
            }
        }
    }
}
