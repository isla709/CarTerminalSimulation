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

    private sealed class ChoiceRandom(int choice) : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int maxValue) => Math.Min(choice, maxValue - 1);
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

    [Theory]
    [InlineData(0, 0, "mergeQueue")]
    [InlineData(1, 0, "mergeAccelerate")]
    [InlineData(2, 0, "overtake")]
    [InlineData(99, -90, "redLight")]
    [InlineData(99, 90, "pedestrianYield")]
    public void Scheduler_GeneratesAllScenarios_WithTurnEligibility(int choice, double angle, string kind)
    {
        var controller = new VehicleEventController(new ChoiceRandom(choice));
        var context = new RouteDrivingContext(100, angle == 0 ? double.PositiveInfinity : 90, angle);
        var incident = controller.Update(16, context, 30, 1000, Options, true);
        Assert.Equal(kind, incident.Kind);
        Assert.InRange(incident.RemainingSeconds, 3, 30);
    }

    [Theory]
    [InlineData("mergeQueue")]
    [InlineData("pedestrianYield")]
    [InlineData("redLight")]
    public void Waiting_CountsDownOnlyAfterStop_AndThenResumes(string kind)
    {
        var controller = new VehicleEventController(new Random(1));
        controller.Start(kind, 3, 100);
        var approaching = controller.Update(1, new(50), 30, 1000, Options, true);
        Assert.Equal(3, approaching.RemainingSeconds);
        Assert.False(approaching.Hold);
        var braking = controller.Update(1, new(100), 5, 1000, Options, true);
        Assert.True(braking.Hold); Assert.Equal(3, braking.RemainingSeconds);
        var stopped = controller.Update(1, new(100), 0, 1000, Options, true);
        Assert.Equal("waiting", stopped.Stage); Assert.Equal(2, stopped.RemainingSeconds);
        Assert.Equal(0, stopped.SpeedLimitKph);
        Assert.Equal("", controller.Update(2, new(100), 0, 1000, Options, true).Kind);
        Assert.Equal("", controller.Update(1, new(100), 30, 1000, Options, true).Kind); // cooldown
    }

    [Fact]
    public void Driver_HoldsStationaryDuringRedLight_ThenAccelerates()
    {
        var driver = new VehicleDriver(Options, new Random(1));
        driver.Events.Start("redLight", 3, 0);
        for (var i = 0; i < 40; i++)
        {
            var state = driver.Tick(.05, 75, 1000, context: new(0));
            Assert.Equal(0, state.SpeedKph); Assert.Equal("waiting", state.Phase);
            Assert.Equal("redLight", state.DrivingEvent); Assert.Equal(0, state.Throttle); Assert.True(state.Brake > 0);
        }
        VehicleTelemetry? result = null;
        for (var i = 0; i < 60; i++) result = driver.Tick(.05, 75, 1000, context: new(0));
        Assert.Equal("", result!.DrivingEvent); Assert.True(result.SpeedKph > 5);
    }

    [Theory]
    [InlineData("mergeAccelerate")]
    [InlineData("overtake")]
    public void AccelerationEvents_RespectCurveAndMaximumLimits_AndExpire(string kind)
    {
        var driver = new VehicleDriver(Options, new Random(1));
        driver.Events.Start(kind, 3, 0, 1.4);
        var boosted = driver.Tick(.05, 300, 1000, context: new(0));
        Assert.InRange(boosted.TargetSpeedKph, 65, 75);
        var corner = driver.Tick(.05, 18, 1000, "turnRight", new(0));
        Assert.Equal(18, corner.TargetSpeedKph);
        Assert.Equal("turnRight", corner.Constraint);
        for (var i = 0; i < 80; i++) driver.Tick(.05, 300, 1000, context: new(0));
        Assert.Equal("", driver.Tick(.05, 300, 1000, context: new(0)).DrivingEvent);
    }

    [Fact]
    public void DisabledOrManualMode_DoesNotInterfere_AndCancelsActiveEvent()
    {
        var driver = new VehicleDriver(Options with { Transmission = "manual", GearRatios = [3.6, 2, 1] }, new Random(1));
        driver.Events.Start("redLight", 20, 0);
        driver.SetControls(new VehicleControls(false, 1, 1, 0));
        var state = driver.Tick(.05, 75, 1000, context: new(0));
        Assert.Equal("", state.DrivingEvent); Assert.Equal("manual", state.ControlMode); Assert.Equal(1, state.Throttle);
        var disabled = new VehicleEventController(new Random(1)); disabled.Start("redLight", 20, 0);
        Assert.Equal("", disabled.Update(1, new(0), 0, 1000, Options with { RandomEventsEnabled = false }, true).Kind);
        Assert.False(new VehicleSimulationOptions().RandomEventsEnabled);
    }

    [Fact]
    public void Scheduler_DoesNotCreateEventsAtRouteEndOrAccelerateWithinBend()
    {
        var controller = new VehicleEventController(new ChoiceRandom(99));
        Assert.Equal("", controller.Update(100, new(0), 30, 50, Options, true).Kind);
        Assert.Equal("mergeQueue", controller.Update(100, new(0, InBend: true), 30, 1000, Options, true).Kind);
    }
}
