using System.Text.Json.Nodes;
using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class TencentMapServiceTests
{
    [Fact]
    public void DecodePolyline_ExpandsTencentDifferentialCoordinates()
    {
        var encoded = new JsonArray(39.984094, 116.307958, 14, 112, -20, 300);

        var points = TencentMapService.DecodePolyline(encoded);

        Assert.Equal(3, points.Count);
        Assert.Equal(39.984094, points[0].Latitude, 6);
        Assert.Equal(116.307958, points[0].Longitude, 6);
        Assert.Equal(39.984108, points[1].Latitude, 6);
        Assert.Equal(116.308070, points[1].Longitude, 6);
        Assert.Equal(39.984088, points[2].Latitude, 6);
        Assert.Equal(116.308370, points[2].Longitude, 6);
    }

    [Fact]
    public void EmbeddedTencentMapKey_CanBeDecryptedAtRuntime()
    {
        var key = TencentMapKeyProvider.GetKey();

        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.True(key.Length >= 16);
    }

    [Fact]
    public void QpsError_ExplainsZeroAllocatedQuota()
    {
        var message = TencentMapApiException.CreateMessage(
            120,
            "驾车路线规划",
            "此key每秒请求量已达到上限",
            "request-1");

        Assert.Contains("已分配额度为 0", message);
        Assert.Contains("0 / 6000", message);
        Assert.Contains("request-1", message);
    }
}
