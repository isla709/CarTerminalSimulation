using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.ViewModels;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class VehicleDrivingEventTests
{
    private static VehicleSimulationOptions Options => VehicleSimulationOptions.Presets["electric"] with
    { Enabled = true, RandomEventsEnabled = true, AverageSpeedKph = 50, MaxSpeedKph = 75 };
    private static RouteSpeedProfile Turn(bool right) => new([
        new GeoPoint { Lat = 30, Lng = 120 }, new GeoPoint { Lat = 30.005, Lng = 120 },
        new GeoPoint { Lat = 30.005, Lng = right ? 120.005 : 119.995 }]);

    [Theory]
    [InlineData(true, "turnRight")]
    [InlineData(false, "turnLeft")]
    public void Turns_BrakeBeforeApex_ThenRaiseLimitImmediately(bool right, string constraint)
    {
        var profile = Turn(right);
        var at = LocationSimulationService.Distance(new() { Lat = 30, Lng = 120 }, new() { Lat = 30.005, Lng = 120 });
        var before = profile.SpeedLimit(at - 5, Options);
        var apex = profile.SpeedLimit(at, Options);
        var after = profile.SpeedLimit(at + 5, Options);
        Assert.Equal(constraint, before.Constraint);
        Assert.Equal("turnExit", apex.Constraint);
        Assert.True(before.Kph < profile.SpeedLimit(at - 100, Options).Kph);
        Assert.True(apex.Kph > before.Kph);
        Assert.True(after.Kph > apex.Kph);
        Assert.Equal(right, profile.DrivingContext(at - 50).TurnAngle > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DrivenTurns_ReachLowSpeedBeforeApex_AndAccelerateOut(bool right)
    {
        var profile = Turn(right);
        var at = LocationSimulationService.Distance(new() { Lat = 30, Lng = 120 }, new() { Lat = 30.005, Lng = 120 });
        var driver = new VehicleDriver(Options with { RandomEventsEnabled = false });
        var readings = new List<(double Distance, VehicleTelemetry State)>();
        var traveled = 0d; var previous = 0d;
        for (var i = 0; i < 4000 && traveled < at + 45; i++)
        {
            var limit = profile.SpeedLimit(traveled, driver.Options);
            var state = driver.Tick(.05, limit.Kph, profile.TotalDistance - traveled, limit.Constraint, profile.DrivingContext(traveled));
            readings.Add((traveled, state));
            traveled += (previous + state.SpeedKph) / 2 / 3.6 * .05; previous = state.SpeedKph;
        }
        var entry = readings.First(x => x.Distance > at - 5).State;
        var exit = readings.First(x => x.Distance > at + 8).State;
        var recovered = readings.First(x => x.Distance > at + 35).State;
        Assert.InRange(entry.SpeedKph, 10, right ? 24 : 29);
        Assert.True(exit.Acceleration > 0, $"Must accelerate from the apex: {exit}");
        Assert.True(recovered.SpeedKph > entry.SpeedKph + 5);
    }

    [Fact]
    public void DenseNavigationCurve_IsOneTurnWithCentralApex()
    {
        static GeoPoint Point(double east, double north) => new() { Lat = 30 + north / 111195, Lng = 120 + east / (111195 * Math.Cos(Math.PI / 6)) };
        var points = new List<GeoPoint> { Point(0, -150) };
        for (var degrees = 0; degrees <= 90; degrees += 10)
        {
            var radians = degrees * Math.PI / 180;
            points.Add(Point(50 * (1 - Math.Cos(radians)), 50 * Math.Sin(radians)));
        }
        points.Add(Point(200, 50));
        var profile = new RouteSpeedProfile(points);
        var context = profile.DrivingContext(100);
        Assert.InRange(context.TurnAngle, 85, 95);
        Assert.InRange(context.TraveledMeters + context.TurnDistanceMeters, 180, 200);
        var apex = context.TraveledMeters + context.TurnDistanceMeters;
        Assert.Equal("turnExit", profile.SpeedLimit(apex + 1, Options).Constraint);
        Assert.Equal("turnExit", profile.SpeedLimit(apex + 10, Options).Constraint);
    }

}
