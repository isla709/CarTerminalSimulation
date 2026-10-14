using System.Text.Json.Nodes;
using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class OldMapServiceTests
{
    [Fact]
    public void PhotonPlaces_ParseWgsCoordinatesAndChineseDetails()
    {
        var root = JsonNode.Parse("""
            {"features":[
              {"properties":{"name":"杭州东站","osm_id":123,"osm_key":"railway","city":"杭州市","district":"上城区"},"geometry":{"coordinates":[120.212,30.29]}},
              {"properties":{"name":"坏坐标"},"geometry":{"coordinates":[999,100]}}
            ]}
            """)!.AsObject();
        var place = Assert.Single(OldMapService.ParsePlaces(root));
        Assert.Equal("杭州东站", place.Title);
        Assert.Equal("杭州市", place.City);
        Assert.Equal("轨道交通", place.Category);
        Assert.Equal(30.29, place.Latitude);
        Assert.Equal(120.212, place.Longitude);
    }

    [Fact]
    public void OsrmRoutes_ParseFullGeometryAndDoNotInventTrafficLightCounts()
    {
        var root = JsonNode.Parse("""
            {"code":"Ok","routes":[{"distance":1100,"duration":125,"geometry":{"coordinates":[[120.1,30.2],[120.2,30.3]]}}]}
            """)!.AsObject();
        var route = Assert.Single(OldMapService.ParseRoutes(root));
        Assert.Equal(1100, route.Distance);
        Assert.Equal(2, route.DurationMinutes);
        Assert.Null(route.TrafficLightCount);
        Assert.Equal(30.2, route.Points[0].Latitude);
        Assert.Equal(120.1, route.Points[0].Longitude);
    }

    [Theory]
    [InlineData("{\"code\":\"NoRoute\"}")]
    [InlineData("{\"code\":\"Ok\",\"routes\":[]}")]
    [InlineData("{\"code\":\"Ok\",\"routes\":[{\"geometry\":{\"coordinates\":[[100,20],[200,95]]}}]}")]
    public void OsrmRoutes_RejectInvalidOrMissingGeometry(string json)
    {
        Assert.Throws<InvalidOperationException>(() => OldMapService.ParseRoutes(JsonNode.Parse(json)!.AsObject()));
    }

    [Fact]
    public async Task HistoryStores_ForOldAndNewRemainIndependent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CTS-MapHistoryTests", Guid.NewGuid().ToString("N"));
        var old = new MapHistoryStore(Path.Combine(directory, "old", "history.json"));
        var current = new MapHistoryStore(Path.Combine(directory, "new", "history.json"));
        await old.AddSearchAsync("旧方案搜索");
        await current.AddSearchAsync("新方案搜索");
        await old.ClearAsync("search");
        Assert.Empty((await old.ReadAsync()).Searches);
        Assert.Equal("新方案搜索", Assert.Single((await current.ReadAsync()).Searches).Query);
    }
}
