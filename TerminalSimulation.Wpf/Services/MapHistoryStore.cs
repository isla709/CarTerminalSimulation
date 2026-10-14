using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerminalSimulation.Wpf.Services;

// Separate from browser cache: app updates and WebView profile changes do not
// discard records. Only one serialized writer may publish a new snapshot.
internal sealed class MapHistoryStore
{
    private readonly AtomicJsonConfigStore<MapHistoryData> _store;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MapHistoryData? _data;

    public MapHistoryStore(string path, Action<string>? warning = null)
    {
        _path = Path.GetFullPath(path);
        _store = new AtomicJsonConfigStore<MapHistoryData>(_path, warning);
    }

    public async Task<MapHistoryData> ReadAsync()
    {
        await _gate.WaitAsync();
        try { return await Task.Run(async () => Clone(await LoadAsync())); }
        finally { _gate.Release(); }
    }

    public async Task<MapHistorySummary> ReadSummaryAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                var data = await LoadAsync();
                return new MapHistorySummary(data.Searches.ToArray(), data.Routes.Select(item => new MapRouteSummary(
                    item.Id, item.Name, item.CreatedAtUtc,
                    item.Route["coordinateSystem"]?.ToString() ?? "gcj02",
                    item.Route["mode"]?.ToString() ?? "draw",
                    (item.Route["points"] as JsonArray)?.Count ?? 0,
                    item.Route["speed"]?.ToString() ?? "60", DescribeSpeed(item.Route))).ToArray());
            });
        }
        finally { _gate.Release(); }
    }

    private static string DescribeSpeed(JsonObject route)
    {
        if (route["vehicle"] is JsonObject vehicle && bool.TryParse(vehicle["enabled"]?.ToString(), out var enabled) && enabled)
            return $"真实驾驶 · 期望 {vehicle["averageSpeedKph"]} / 上限 {vehicle["maxSpeedKph"]} km/h";
        return $"固定 {route["speed"]?.ToString() ?? "60"} km/h";
    }

    public Task AddSearchAsync(string query) => MutateAsync(data =>
    {
        query = query.Trim();
        if (query.Length is < 2 or > 120) throw new InvalidDataException("搜索词长度必须为 2–120 个字符");
        data.Searches.RemoveAll(item => string.Equals(item.Query, query, StringComparison.OrdinalIgnoreCase));
        data.Searches.Insert(0, new MapSearchHistory { Query = query });
        data.Searches = data.Searches.Take(50).ToList();
    });

    public Task AddRouteAsync(string name, JsonObject route, bool automatic = false) => MutateAsync(data =>
    {
        name = ValidateName(name);
        if (route["points"] is not JsonArray points || points.Count is < 1 or > 100_000)
            throw new InvalidDataException("轨迹点数量无效");
        var shape = RouteShape.Read(route) ?? throw new InvalidDataException("轨迹含无效坐标或坐标系");
        var matches = data.Routes.Where(item => shape.Matches(RouteShape.Read(item.Route))).ToList();
        var existing = matches.FirstOrDefault();
        foreach (var duplicate in matches) data.Routes.Remove(duplicate);
        data.Routes.Insert(0, new MapRouteHistory
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = automatic && existing is not null ? existing.Name : name,
            Route = (JsonObject)route.DeepClone()
        });
        data.Routes = data.Routes.Take(30).ToList();
    });

    public Task RenameRouteAsync(string id, string name) => MutateAsync(data =>
    {
        var route = data.Routes.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidDataException("轨迹记录已不存在");
        route.Name = ValidateName(name);
    });

    public Task DeleteAsync(string kind, string id) => MutateAsync(data =>
    {
        if (kind == "search") data.Searches.RemoveAll(item => item.Id == id);
        else if (kind == "route") data.Routes.RemoveAll(item => item.Id == id);
        else throw new InvalidDataException("未知记录类型");
    });

    public Task ClearAsync(string kind) => MutateAsync(data =>
    {
        if (kind == "search") data.Searches.Clear();
        else if (kind == "route") data.Routes.Clear();
        else throw new InvalidDataException("未知记录类型");
    });

    private async Task MutateAsync(Action<MapHistoryData> mutation)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(async () =>
            {
                var next = Clone(await LoadAsync());
                mutation(next);
                await _store.SaveAsync(next);
                _data = next; // A failed disk write must not appear to have succeeded.
            });
        }
        finally { _gate.Release(); }
    }

    private async Task<MapHistoryData> LoadAsync()
    {
        if (_data is not null) return _data;
        var data = _store.Load();
        data.Searches = (data.Searches ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Query)).Take(50).ToList();
        var routes = (data.Routes ?? []).Where(item => item is not null && item.Route is not null)
            .OrderByDescending(item => item.CreatedAtUtc).ToList();
        var unique = new List<(MapRouteHistory Item, RouteShape? Shape)>();
        var removed = false;
        foreach (var item in routes)
        {
            var shape = RouteShape.Read(item.Route);
            if (shape is not null && unique.Any(saved => shape.Matches(saved.Shape))) { removed = true; continue; }
            unique.Add((item, shape));
        }
        data.Routes = unique.Select(item => item.Item).Take(30).ToList();
        if (removed)
        {
            // Dedicated migration backup must not be overwritten by later saves.
            if (File.Exists(_path)) File.Copy(_path, $"{_path}.dedupe-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bak");
            await _store.SaveAsync(data);
        }
        return _data = data;
    }

    private sealed record RouteShape(string Mode, GeoCoordinate[] Points)
    {
        public static RouteShape? Read(JsonObject route)
        {
            if (route["points"] is not JsonArray points || points.Count is < 1 or > 100_000) return null;
            var system = (route["coordinateSystem"]?.ToString() ?? "gcj02").ToLowerInvariant() switch
            {
                "gcj02" or "gcj-02" => GeoCoordinateSystem.Gcj02,
                "wgs84" or "wgs-84" => GeoCoordinateSystem.Wgs84,
                "bd09" or "bd-09" => GeoCoordinateSystem.Bd09,
                _ => (GeoCoordinateSystem?)null
            };
            if (system is null) return null;
            var normalized = new GeoCoordinate[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] is not JsonObject point ||
                    !double.TryParse(point["lat"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(point["lng"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng) ||
                    !double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180) return null;
                // Comparison only: never replace the stored coordinate values/system.
                normalized[i] = GeoCoordinateConverter.Convert(lat, lng, system.Value, GeoCoordinateSystem.Wgs84);
            }
            return new((route["mode"]?.ToString() ?? "draw").ToLowerInvariant(), normalized);
        }

        public bool Matches(RouteShape? other)
        {
            if (other is null || Mode != other.Mode || Points.Length != other.Points.Length) return false;
            for (var i = 0; i < Points.Length; i++)
            {
                var north = (Points[i].Latitude - other.Points[i].Latitude) * 111195;
                var east = (Points[i].Longitude - other.Points[i].Longitude) * 111195 * Math.Cos(Points[i].Latitude * Math.PI / 180);
                if (north * north + east * east > .5 * .5) return false;
            }
            return true;
        }
    }

    private static MapHistoryData Clone(MapHistoryData data) =>
        JsonSerializer.Deserialize<MapHistoryData>(JsonSerializer.Serialize(data))!;

    private static string ValidateName(string name) =>
        name.Trim() is { Length: > 0 and <= 80 } value ? value : throw new InvalidDataException("名称必须为 1–80 个字符");
}

internal sealed class MapHistoryData
{
    public int Version { get; set; } = 1;
    public List<MapSearchHistory> Searches { get; set; } = [];
    public List<MapRouteHistory> Routes { get; set; } = [];
}

internal sealed record MapHistorySummary(IReadOnlyList<MapSearchHistory> Searches, IReadOnlyList<MapRouteSummary> Routes);
internal sealed record MapRouteSummary(string Id, string Name, DateTimeOffset CreatedAtUtc,
    string CoordinateSystem, string Mode, int PointCount, string Speed, string SpeedDescription);

internal sealed class MapSearchHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Query { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class MapRouteHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public JsonObject Route { get; set; } = new();
}
