using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace TerminalSimulation.Wpf.Services;

// Independent Leaflet services: Photon returns WGS-84 places and OSRM routes.
// Nothing in this service reads or transmits the Tencent credential.
internal sealed class OldMapService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim SearchGate = new(1, 1);
    private static DateTimeOffset _lastSearch;
    private static readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<OldPlaceResult> Places)> Cache = new();

    public async Task<IReadOnlyList<OldPlaceResult>> SearchAsync(string query, double latitude, double longitude, CancellationToken token)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 120) return [];
        var cacheKey = FormattableString.Invariant($"{query}|{latitude:F3}|{longitude:F3}");
        await SearchGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(10)) return cached.Places;
            var delay = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - _lastSearch);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            _lastSearch = DateTimeOffset.UtcNow;
            var uri = FormattableString.Invariant($"https://photon.komoot.io/api/?q={Uri.EscapeDataString(query)}&lat={latitude:F6}&lon={longitude:F6}&limit=8");
            var places = ParsePlaces(await GetJsonAsync(uri, token).ConfigureAwait(false));
            if (Cache.Count >= 40) Cache.Remove(Cache.MinBy(item => item.Value.At).Key);
            Cache[cacheKey] = (DateTimeOffset.UtcNow, places);
            return places;
        }
        finally { SearchGate.Release(); }
    }

    public async Task<IReadOnlyList<OldRouteResult>> PlanAsync(double startLat, double startLng, double endLat, double endLng, CancellationToken token)
    {
        var uri = FormattableString.Invariant($"https://router.project-osrm.org/route/v1/driving/{startLng:F6},{startLat:F6};{endLng:F6},{endLat:F6}?overview=full&geometries=geojson&steps=false&alternatives=true");
        return ParseRoutes(await GetJsonAsync(uri, token).ConfigureAwait(false));
    }

    internal static IReadOnlyList<OldPlaceResult> ParsePlaces(JsonObject root)
    {
        var results = new List<OldPlaceResult>();
        foreach (var node in root["features"]?.AsArray() ?? [])
        {
            if (node?["properties"] is not JsonObject properties || node["geometry"]?["coordinates"] is not JsonArray coordinates || coordinates.Count < 2) continue;
            if (!ReadCoordinate(coordinates[1], coordinates[0], out var lat, out var lng)) continue;
            var name = properties["name"]?.ToString() ?? properties["street"]?.ToString() ?? "未命名地点";
            var address = string.Join(" · ", new[] { properties["state"]?.ToString(), properties["city"]?.ToString(), properties["district"]?.ToString(), properties["street"]?.ToString(), properties["housenumber"]?.ToString() }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct());
            var category = properties["osm_key"]?.ToString() switch
            {
                "highway" => "道路", "railway" => "轨道交通", "shop" => "商铺", "tourism" => "景点",
                "amenity" => "公共设施", "building" => "建筑", "place" => "地点", _ => "地点"
            };
            results.Add(new OldPlaceResult(properties["osm_id"]?.ToString() ?? "", name, address, category,
                properties["city"]?.ToString() ?? "", properties["district"]?.ToString() ?? "", lat, lng));
        }
        return results;
    }

    internal static IReadOnlyList<OldRouteResult> ParseRoutes(JsonObject root)
    {
        if (root["code"]?.ToString() != "Ok") throw new InvalidOperationException("导航服务未找到可通行路线");
        var routes = new List<OldRouteResult>();
        foreach (var node in root["routes"]?.AsArray() ?? [])
        {
            if (node?["geometry"]?["coordinates"] is not JsonArray coordinates) continue;
            var points = new List<OldMapPoint>();
            foreach (var point in coordinates)
            {
                if (point is not JsonArray values || values.Count < 2 || !ReadCoordinate(values[1], values[0], out var lat, out var lng))
                    throw new InvalidOperationException("导航服务返回了无效坐标");
                points.Add(new OldMapPoint(lat, lng));
            }
            if (points.Count is < 2 or > 100_000) continue;
            _ = double.TryParse(node["distance"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance);
            _ = double.TryParse(node["duration"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
            routes.Add(new OldRouteResult(Math.Max(0, (int)Math.Round(distance)), Math.Max(1, (int)Math.Round(duration / 60)), points));
        }
        if (routes.Count == 0) throw new InvalidOperationException("导航服务未返回有效路线");
        return routes;
    }

    private static bool ReadCoordinate(JsonNode? latNode, JsonNode? lngNode, out double lat, out double lng)
    {
        lat = lng = 0;
        return double.TryParse(latNode?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
               double.TryParse(lngNode?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out lng) &&
               double.IsFinite(lat) && double.IsFinite(lng) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
    }

    private static async Task<JsonObject> GetJsonAsync(string uri, CancellationToken token)
    {
        using var response = await Client.GetAsync(uri, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false))?.AsObject()
            ?? throw new InvalidOperationException("地图服务返回了空数据");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"CarTerminalSimulation/{AppVersionInfo.AppVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.7");
        return client;
    }
}

internal sealed record OldPlaceResult(string Id, string Title, string Address, string Category, string City, string District, double Latitude, double Longitude);
internal sealed record OldMapPoint(double Latitude, double Longitude);
internal sealed record OldRouteResult(int Distance, int DurationMinutes, IReadOnlyList<OldMapPoint> Points)
{
    public int? TrafficLightCount => null; // OSRM does not supply traffic-light counts.
    public string[] Tags => ["道路导航"];
}
