using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.ViewModels;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class LocationSimulationServiceTests
{
    [Fact]
    public async Task RunAsync_AdvancesAlongRouteAndReportsBearing()
    {
        var service = new LocationSimulationService();
        var updates = new List<(GeoPoint Point, int Bearing)>();
        var path = new[] { new GeoPoint { Lat = 39.9, Lng = 116.4 }, new GeoPoint { Lat = 39.90001, Lng = 116.40001 } };
        await service.RunAsync(path, () => 120, (point, bearing) => updates.Add((point, bearing)), CancellationToken.None);
        Assert.NotEmpty(updates);
        Assert.All(updates, update => Assert.InRange(update.Bearing, 0, 359));
    }

    [Fact]
    public async Task RunAsync_ObservesCancellation()
    {
        var service = new LocationSimulationService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var path = new[] { new GeoPoint { Lat = 39.9, Lng = 116.4 }, new GeoPoint { Lat = 40.9, Lng = 117.4 } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(path, () => 1, (_, _) => { }, cts.Token));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 90)]
    [InlineData(-1, 0, 180)]
    [InlineData(0, -1, 270)]
    public void Bearing_UsesClockwiseDegreesFromNorth(double latitude, double longitude, int expected)
    {
        var from = new GeoPoint { Lat = 0, Lng = 0 };
        var to = new GeoPoint { Lat = latitude, Lng = longitude };

        Assert.Equal(expected, LocationSimulationService.Bearing(from, to));
    }
}
