using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace TerminalSimulation.Wpf.Services;

internal sealed class TencentMapService
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://apis.map.qq.com/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly Func<string> _keyProvider;

    public TencentMapService(Func<string> keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public async Task<IReadOnlyList<TencentPlaceResult>> SearchAsync(
        string keyword,
        double centerLatitude,
        double centerLongitude,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];
        var center = FormattableString.Invariant($"{centerLatitude:F6},{centerLongitude:F6}");
        var requestUri = "ws/place/v1/search?output=json&page_size=12&get_subpois=1" +
                         $"&keyword={Uri.EscapeDataString(keyword.Trim())}" +
                         $"&boundary={Uri.EscapeDataString($"nearby({center},1000,1)")}" +
                         $"&key={Uri.EscapeDataString(_keyProvider())}";
        var root = await GetJsonAsync(requestUri, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(root, "地点搜索");
        var results = new List<TencentPlaceResult>();
        foreach (var item in root["data"]?.AsArray() ?? [])
        {
            if (item is not JsonObject place || place["location"] is not JsonObject location) continue;
            if (!TryReadDouble(location["lat"], out var latitude) ||
                !TryReadDouble(location["lng"], out var longitude)) continue;
            var adInfo = place["ad_info"] as JsonObject;
            results.Add(new TencentPlaceResult(
                place["id"]?.ToString() ?? string.Empty,
                place["title"]?.ToString() ?? "未命名地点",
                place["address"]?.ToString() ?? string.Empty,
                place["category"]?.ToString() ?? "地点",
                adInfo?["province"]?.ToString() ?? string.Empty,
                adInfo?["city"]?.ToString() ?? string.Empty,
                adInfo?["district"]?.ToString() ?? string.Empty,
                latitude,
                longitude));
        }
        return results;
    }

    public async Task<IReadOnlyList<TencentRouteResult>> PlanDrivingAsync(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude,
        string? startPoiId,
        string? endPoiId,
        CancellationToken cancellationToken = default)
    {
        var from = FormattableString.Invariant($"{startLatitude:F6},{startLongitude:F6}");
        var to = FormattableString.Invariant($"{endLatitude:F6},{endLongitude:F6}");
        var requestUri = "ws/direction/v1/driving/?output=json&get_mp=1&get_speed=1&no_step=0" +
                         "&policy=LEAST_TIME,REAL_TRAFFIC,NAV_POINT_FIRST" +
                         $"&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}" +
                         (string.IsNullOrWhiteSpace(startPoiId) ? string.Empty : $"&from_poi={Uri.EscapeDataString(startPoiId)}") +
                         (string.IsNullOrWhiteSpace(endPoiId) ? string.Empty : $"&to_poi={Uri.EscapeDataString(endPoiId)}") +
                         $"&key={Uri.EscapeDataString(_keyProvider())}";
        var root = await GetJsonAsync(requestUri, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(root, "驾车路线规划");
        var routes = new List<TencentRouteResult>();
        var routeIndex = 0;
        foreach (var item in root["result"]?["routes"]?.AsArray() ?? [])
        {
            if (item is not JsonObject route || route["polyline"] is not JsonArray polyline) continue;
            var points = DecodePolyline(polyline);
            if (points.Count < 2) continue;
            var steps = new List<TencentRouteStep>();
            foreach (var stepNode in route["steps"]?.AsArray() ?? [])
            {
                if (stepNode is not JsonObject step) continue;
                steps.Add(new TencentRouteStep(
                    step["instruction"]?.ToString() ?? string.Empty,
                    step["road_name"]?.ToString() ?? string.Empty,
                    ReadInt(step["distance"]),
                    step["act_desc"]?.ToString() ?? string.Empty));
            }
            var tags = route["tags"]?.AsArray()
                .Select(static tag => tag?.ToString())
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Cast<string>()
                .ToArray() ?? [];
            routes.Add(new TencentRouteResult(
                routeIndex++,
                ReadInt(route["distance"]),
                ReadInt(route["duration"]),
                ReadInt(route["traffic_light_count"]),
                ReadDouble(route["toll"]),
                tags,
                points,
                steps));
        }
        if (routes.Count == 0) throw new InvalidOperationException("腾讯地图未返回可用的驾车路线");
        return routes;
    }

    public async Task<TencentIpLocation> LocateByIpAsync(CancellationToken cancellationToken = default)
    {
        var requestUri = $"ws/location/v1/ip?output=json&key={Uri.EscapeDataString(_keyProvider())}";
        var root = await GetJsonAsync(requestUri, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(root, "IP 定位");
        var result = root["result"]?.AsObject() ?? throw new InvalidOperationException("腾讯地图 IP 定位缺少结果");
        var location = result["location"]?.AsObject() ?? throw new InvalidOperationException("腾讯地图 IP 定位缺少坐标");
        if (!TryReadDouble(location["lat"], out var latitude) || !TryReadDouble(location["lng"], out var longitude))
            throw new InvalidOperationException("腾讯地图 IP 定位坐标无效");
        var adInfo = result["ad_info"] as JsonObject;
        var label = string.Join(" · ", new[]
        {
            adInfo?["province"]?.ToString(), adInfo?["city"]?.ToString(), adInfo?["district"]?.ToString()
        }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        return new TencentIpLocation(latitude, longitude, label);
    }

    internal static IReadOnlyList<TencentMapPoint> DecodePolyline(JsonArray encoded)
    {
        var values = encoded.Select(ReadDouble).ToArray();
        if (values.Length < 4 || values.Length % 2 != 0) return [];
        for (var index = 2; index < values.Length; index++)
            values[index] = values[index - 2] + values[index] / 1_000_000d;
        var points = new List<TencentMapPoint>(values.Length / 2);
        for (var index = 0; index < values.Length; index += 2)
            points.Add(new TencentMapPoint(values[index], values[index + 1]));
        return points;
    }

    private static async Task<JsonObject> GetJsonAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("腾讯地图返回了空响应");
    }

    private static void EnsureSuccess(JsonObject root, string operation)
    {
        var status = ReadInt(root["status"]);
        if (status == 0) return;
        var providerMessage = root["message"]?.ToString() ?? "未知错误";
        var requestId = root["request_id"]?.ToString();
        throw new TencentMapApiException(status, operation, providerMessage, requestId);
    }

    private static bool TryReadDouble(JsonNode? node, out double value) =>
        double.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double ReadDouble(JsonNode? node) => TryReadDouble(node, out var value) ? value : 0;

    private static int ReadInt(JsonNode? node) =>
        int.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

internal sealed record TencentPlaceResult(string Id, string Title, string Address, string Category,
    string Province, string City, string District, double Latitude, double Longitude);

internal sealed record TencentMapPoint(double Latitude, double Longitude);

internal sealed record TencentRouteStep(string Instruction, string RoadName, int Distance, string Action);

internal sealed record TencentRouteResult(int Index, int Distance, int DurationMinutes, int TrafficLightCount,
    double Toll, IReadOnlyList<string> Tags, IReadOnlyList<TencentMapPoint> Points,
    IReadOnlyList<TencentRouteStep> Steps);

internal sealed record TencentIpLocation(double Latitude, double Longitude, string Label);

internal sealed class TencentMapApiException : InvalidOperationException
{
    public TencentMapApiException(int status, string operation, string providerMessage, string? requestId)
        : base(CreateMessage(status, operation, providerMessage, requestId))
    {
        Status = status;
        RequestId = requestId;
    }

    public int Status { get; }
    public string? RequestId { get; }

    internal static string CreateMessage(int status, string operation, string providerMessage, string? requestId)
    {
        var guidance = status switch
        {
            120 => "该 Key 的 WebService 每秒配额为 0 或瞬时请求超限。请在腾讯位置服务控制台为此 Key 分配对应接口额度；控制台中的“0 / 6000”表示已分配额度为 0，并非已调用 0 次。",
            121 => "该 Key 的 WebService 日配额已用完，请检查并调整接口额度分配。",
            110 => "请求来源未获授权，请检查 WebService 的域名、IP 白名单或签名校验配置。",
            113 => "该 Key 未开通此 WebService 接口权限，请在腾讯位置服务控制台启用对应产品。",
            190 => "腾讯地图 Key 无效、已删除或已被禁用。",
            _ => providerMessage
        };
        var requestSuffix = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $"（请求 ID：{requestId}）";
        return $"{operation}失败：{guidance}{requestSuffix}";
    }
}
