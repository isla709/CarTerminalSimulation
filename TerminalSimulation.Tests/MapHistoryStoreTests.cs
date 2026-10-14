using System.Text.Json.Nodes;
using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class MapHistoryStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "CTS-MapHistoryTests", Guid.NewGuid().ToString("N"), "history.json");

    [Fact]
    public async Task SearchHistory_PersistsDeduplicatesAndDeletes()
    {
        var path = NewPath();
        var store = new MapHistoryStore(path);
        await store.AddSearchAsync("北京站");
        await store.AddSearchAsync("上海站");
        await store.AddSearchAsync(" 北京站 ");
        var snapshot = await new MapHistoryStore(path).ReadAsync();
        Assert.Equal(new[] { "北京站", "上海站" }, snapshot.Searches.Select(item => item.Query));
        await store.DeleteAsync("search", snapshot.Searches[0].Id);
        Assert.Single((await new MapHistoryStore(path).ReadAsync()).Searches);
        await store.ClearAsync("search");
        Assert.Empty((await new MapHistoryStore(path).ReadAsync()).Searches);
    }

    [Fact]
    public async Task RouteHistory_PreservesCoordinatesModeSpeedAndIsolatedSnapshots()
    {
        var path = NewPath();
        var store = new MapHistoryStore(path);
        var route = JsonNode.Parse("""
            {"mode":"draw","coordinateSystem":"wgs84","speed":88,"points":[{"lat":30.2,"lng":120.1},{"lat":30.3,"lng":120.2}]}
            """)!.AsObject();
        await store.AddRouteAsync("测试路线", route);
        route["speed"] = 1;
        var snapshot = await store.ReadAsync();
        var saved = Assert.Single(snapshot.Routes);
        Assert.Equal("wgs84", saved.Route["coordinateSystem"]!.ToString());
        Assert.Equal("draw", saved.Route["mode"]!.ToString());
        Assert.Equal(88, saved.Route["speed"]!.GetValue<int>());
        var summary = await store.ReadSummaryAsync();
        Assert.Equal(2, Assert.Single(summary.Routes).PointCount);
        Assert.Equal("88", summary.Routes[0].Speed);
        saved.Route["points"] = new JsonArray();
        Assert.Equal(2, (await store.ReadAsync()).Routes[0].Route["points"]!.AsArray().Count);
        await store.RenameRouteAsync(saved.Id, "新名称");
        Assert.Equal("新名称", (await new MapHistoryStore(path).ReadAsync()).Routes[0].Name);
        await store.DeleteAsync("route", saved.Id);
        Assert.Empty((await new MapHistoryStore(path).ReadAsync()).Routes);
    }

    [Fact]
    public async Task RouteSummary_DescribesVehicleProfileInsteadOfIgnoredFixedSpeed()
    {
        var store = new MapHistoryStore(NewPath());
        var route = JsonNode.Parse("""
            {"mode":"draw","coordinateSystem":"gcj02","speed":88,"vehicle":{"enabled":true,"averageSpeedKph":45,"maxSpeedKph":70},"points":[{"lat":30,"lng":120},{"lat":30.01,"lng":120}]}
            """)!.AsObject();
        await store.AddRouteAsync("车辆轨迹", route);
        var summary = Assert.Single((await store.ReadSummaryAsync()).Routes);
        Assert.Equal("真实驾驶 · 期望 45 / 上限 70 km/h", summary.SpeedDescription);
        Assert.Equal("88", summary.Speed);
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotDropSearches_AndEnforceLimits()
    {
        var store = new MapHistoryStore(NewPath());
        await Task.WhenAll(Enumerable.Range(0, 60).Select(index => store.AddSearchAsync($"地点{index}")));
        Assert.Equal(50, (await store.ReadAsync()).Searches.Count);
        var route = JsonNode.Parse("""{"mode":"single","coordinateSystem":"gcj02","points":[{"lat":20,"lng":100}]}""")!.AsObject();
        await Task.WhenAll(Enumerable.Range(0, 35).Select(index =>
        {
            var distinct = (JsonObject)route.DeepClone();
            distinct["points"]![0]!["lat"] = 20 + index * .001;
            return store.AddRouteAsync($"轨迹{index}", distinct);
        }));
        Assert.Equal(30, (await store.ReadAsync()).Routes.Count);
        await store.ClearAsync("route");
        Assert.Equal(50, (await store.ReadAsync()).Searches.Count);
    }

    [Fact]
    public async Task SameRoute_UpdatesLatestProfile_PreservesIdAndAutomaticCustomName()
    {
        var store = new MapHistoryStore(NewPath());
        var route = JsonNode.Parse("""{"mode":"draw","coordinateSystem":"gcj02","speed":60,"points":[{"lat":30,"lng":120},{"lat":30.01,"lng":120}]}""")!.AsObject();
        await store.AddRouteAsync("初始名称", route);
        var first = Assert.Single((await store.ReadAsync()).Routes);
        await store.RenameRouteAsync(first.Id, "我的通勤路线");
        route["speed"] = 88;
        route["savedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        route["applicationVersion"] = "preview10-new-build";
        route["vehicle"] = JsonNode.Parse("""{"enabled":true,"maxSpeedKph":100,"averageSpeedKph":70}""");
        route["points"]![0]!["name"] = "新的地点名称";
        await store.AddRouteAsync("自动生成名称", route, automatic: true);
        var updated = Assert.Single((await store.ReadAsync()).Routes);
        Assert.Equal(first.Id, updated.Id); Assert.Equal("我的通勤路线", updated.Name);
        Assert.True(updated.CreatedAtUtc >= first.CreatedAtUtc);
        Assert.Equal(70, updated.Route["vehicle"]!["averageSpeedKph"]!.GetValue<int>());
        await store.AddRouteAsync("手动保存新名称", route);
        Assert.Equal("手动保存新名称", Assert.Single((await store.ReadAsync()).Routes).Name);
    }

    [Fact]
    public async Task CoordinateEquivalents_MergeWithoutChangingLatestStoredCoordinates()
    {
        var store = new MapHistoryStore(NewPath());
        var route = JsonNode.Parse("""{"mode":"draw","coordinateSystem":"wgs84","points":[{"lat":30,"lng":120},{"lat":30.01,"lng":120}]}""")!.AsObject();
        await store.AddRouteAsync("路线", route);
        var converted = (JsonObject)route.DeepClone(); converted["coordinateSystem"] = "gcj02";
        foreach (var point in converted["points"]!.AsArray())
        {
            var value = GeoCoordinateConverter.Convert(point!["lat"]!.GetValue<double>(), point["lng"]!.GetValue<double>(), GeoCoordinateSystem.Wgs84, GeoCoordinateSystem.Gcj02);
            point["lat"] = Math.Round(value.Latitude, 6); point["lng"] = Math.Round(value.Longitude, 6);
        }
        await store.AddRouteAsync("坐标已转换", converted);
        var saved = Assert.Single((await store.ReadAsync()).Routes);
        Assert.Equal("gcj02", saved.Route["coordinateSystem"]!.ToString());
        Assert.Equal(converted["points"]!.ToJsonString(), saved.Route["points"]!.ToJsonString());
        // Direct reinterpretation is a different map position, not a conversion.
        route["coordinateSystem"] = "gcj02";
        await store.AddRouteAsync("直接切换的路线", route);
        Assert.Equal(2, (await store.ReadAsync()).Routes.Count);
    }

    [Fact]
    public async Task DifferentPathsDirectionsAndModes_AreNotMergedByName()
    {
        var store = new MapHistoryStore(NewPath());
        var route = JsonNode.Parse("""{"mode":"draw","coordinateSystem":"gcj02","points":[{"lat":30,"lng":120},{"lat":30.01,"lng":120}]}""")!.AsObject();
        await store.AddRouteAsync("同名路线", route);
        var different = (JsonObject)route.DeepClone(); different["points"]![1]!["lng"] = 120.001;
        await store.AddRouteAsync("同名路线", different);
        var reverse = (JsonObject)route.DeepClone(); reverse["points"] = new JsonArray(route["points"]!.AsArray().Reverse().Select(point => point!.DeepClone()).ToArray());
        await store.AddRouteAsync("同名路线", reverse);
        route["mode"] = "navigation"; await store.AddRouteAsync("同名路线", route);
        Assert.Equal(4, (await store.ReadAsync()).Routes.Count);
    }

    [Fact]
    public async Task ExistingDuplicates_AreMigratedOnce_KeepNewestAndRecoveryBackup()
    {
        var path = NewPath();
        var route = JsonNode.Parse("""{"mode":"draw","coordinateSystem":"gcj02","points":[{"lat":30,"lng":120},{"lat":30.01,"lng":120}]}""")!.AsObject();
        var different = (JsonObject)route.DeepClone(); different["points"]![1]!["lat"] = 30.02;
        var raw = new MapHistoryData { Routes = [
            new() { Id = "old", Name = "旧参数", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), Route = (JsonObject)route.DeepClone() },
            new() { Id = "latest", Name = "最新参数", CreatedAtUtc = DateTimeOffset.UtcNow, Route = (JsonObject)route.DeepClone() },
            new() { Name = "其他路线", Route = different }] };
        raw.Routes[1].Route["speed"] = 88;
        await new AtomicJsonConfigStore<MapHistoryData>(path).SaveAsync(raw);
        var migrated = await new MapHistoryStore(path).ReadAsync();
        Assert.Equal(2, migrated.Routes.Count);
        Assert.DoesNotContain(migrated.Routes, item => item.Id == "old");
        Assert.Contains(migrated.Routes, item => item.Id == "latest" && item.Route["speed"]!.GetValue<int>() == 88);
        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "history.json.dedupe-*.bak"));
        Assert.Equal(3, new AtomicJsonConfigStore<MapHistoryData>(backup).Load().Routes.Count);
        Assert.Equal(2, (await new MapHistoryStore(path).ReadAsync()).Routes.Count);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "history.json.dedupe-*.bak"));
    }

    [Fact]
    public async Task ConcurrentIdenticalSaves_ProduceOneRecord()
    {
        var store = new MapHistoryStore(NewPath());
        var route = JsonNode.Parse("""{"mode":"single","coordinateSystem":"gcj02","points":[{"lat":20,"lng":100}]}""")!.AsObject();
        await Task.WhenAll(Enumerable.Range(0, 35).Select(index => store.AddRouteAsync($"轨迹{index}", route)));
        Assert.Single((await store.ReadAsync()).Routes);
    }

    [Fact]
    public async Task InvalidMutation_DoesNotChangePersistedData()
    {
        var path = NewPath();
        var store = new MapHistoryStore(path);
        await store.AddSearchAsync("原记录");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddSearchAsync("x"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ClearAsync("unknown"));
        Assert.Single((await new MapHistoryStore(path).ReadAsync()).Searches);
    }
}
