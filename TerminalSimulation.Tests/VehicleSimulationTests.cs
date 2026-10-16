using System.Text.Json;
using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.ViewModels;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class VehicleSimulationTests
{
    private static VehicleTelemetry Drive(VehicleDriver driver, double seconds, double limit = 300, double remaining = 10000)
    {
        VehicleTelemetry? result = null;
        for (var tick = 0; tick < seconds * 20; tick++) result = driver.Tick(.05, limit, remaining);
        return result!;
    }

    [Fact]
    public void Presets_AreValidAndKeepIndependentRatioArrays()
    {
        foreach (var preset in VehicleSimulationOptions.Presets.Values)
        {
            var copy = preset.Validate();
            Assert.NotSame(preset.GearRatios, copy.GearRatios);
            Assert.InRange(copy.AverageSpeedKph, 1, copy.MaxSpeedKph);
        }
        Assert.Equal(10, VehicleSimulationOptions.Presets["manual-truck"].GearRatios.Length);
    }

    [Fact]
    public void Options_RejectUnsafeOrNonFiniteParameters()
    {
        Assert.Throws<ArgumentException>(() => (new VehicleSimulationOptions { AverageSpeedKph = 101 }).Validate());
        Assert.Throws<ArgumentException>(() => (new VehicleSimulationOptions { MaxAcceleration = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (new VehicleSimulationOptions { GearRatios = [1, 2] }).Validate());
        Assert.Throws<ArgumentException>(() => (new VehicleSimulationOptions { UpshiftRpm = 1000 }).Validate());
        Assert.Throws<ArgumentException>(() => (new VehicleSimulationOptions { Powertrain = "unknown" }).Validate());
    }

    [Fact]
    public void Automatic_StartsSmoothly_ChangesGears_AndVariesCruiseSpeed()
    {
        var driver = new VehicleDriver(new VehicleSimulationOptions { Enabled = true, MaxSpeedKph = 70, AverageSpeedKph = 55 });
        var first = driver.Tick(.05, 70, 10000);
        Assert.InRange(first.SpeedKph, 0, .1);
        var readings = Enumerable.Range(0, 2400).Select(_ => driver.Tick(.05, 70, 10000)).ToArray();
        Assert.All(readings, t => Assert.InRange(t.SpeedKph, 0, 70));
        Assert.All(readings, t => Assert.InRange(t.Acceleration, -6, 2.6));
        Assert.Contains(readings, t => t.Phase == "shifting");
        Assert.Contains(readings, t => t.Gear > 1);
        var cruise = readings.Skip(1200).Select(t => t.SpeedKph).ToArray();
        Assert.InRange(cruise.Average(), 50, 60);
        Assert.True(cruise.Max() - cruise.Min() > 1, "Cruise must not remain a perfectly fixed number");
    }

    [Fact]
    public void RouteLimitsAndEndpoint_CauseBraking()
    {
        var driver = new VehicleDriver(new VehicleSimulationOptions { Enabled = true });
        var fast = Drive(driver, 35);
        var corner = Drive(driver, 12, 18);
        Assert.True(fast.SpeedKph > 40);
        Assert.InRange(corner.SpeedKph, 16, 20);
        var stop = Drive(driver, 12, 300, .1);
        Assert.InRange(stop.SpeedKph, 0, .5);
        Assert.Equal("finish", stop.Constraint);
    }

    [Fact]
    public void Manual_NeutralProducesNoTraction_ThrottleAndBrakeWork()
    {
        var driver = new VehicleDriver(VehicleSimulationOptions.Presets["small-manual"]);
        driver.SetControls(new VehicleControls(false, 0, 1, 0));
        var neutral = Drive(driver, 2);
        Assert.Equal(0, neutral.SpeedKph);
        Assert.Equal("neutral", neutral.Phase);
        driver.SetControls(new VehicleControls(false, 1, .8, 0));
        var accelerated = Drive(driver, 4);
        Assert.True(accelerated.SpeedKph > 10);
        Assert.Equal("manual", accelerated.ControlMode);
        driver.SetControls(new VehicleControls(false, 1, 0, 1));
        var stopped = Drive(driver, 3);
        Assert.Equal(0, stopped.SpeedKph);
        driver.SetControls(new VehicleControls(true));
        Assert.Equal("auto", driver.Tick(.05, 100, 10000).ControlMode);
    }

    [Fact]
    public void Manual_ShiftsCutTraction_UnsafeDownshiftIsRejected()
    {
        var driver = new VehicleDriver(VehicleSimulationOptions.Presets["small-manual"] with { AverageSpeedKph = 80 });
        Drive(driver, 70);
        var before = driver.Tick(.05, 300, 10000);
        Assert.True(before.Gear > 1);
        driver.SetControls(new VehicleControls(false, 1, 1, 0));
        var protectedShift = driver.Tick(.05, 300, 10000);
        Assert.Equal(before.Gear, protectedShift.Gear);
        Assert.Equal("shiftProtected", protectedShift.Constraint);
        driver.SetControls(new VehicleControls(false, 0, 1, 0));
        var shift = driver.Tick(.05, 300, 10000);
        Assert.Equal("shifting", shift.Phase);
        Assert.True(shift.Acceleration <= 0);
    }

    [Fact]
    public void NonManualTransmission_ForcesAutomaticControls()
    {
        var driver = new VehicleDriver(VehicleSimulationOptions.Presets["electric"]);
        driver.SetControls(new VehicleControls(false, 0, 0, 1));
        var result = Drive(driver, 5);
        Assert.Equal("auto", result.ControlMode);
        Assert.Equal(1, result.Gear);
        Assert.True(result.SpeedKph > 20);
        Assert.True(result.SpeedKph > Drive(new VehicleDriver(VehicleSimulationOptions.Presets["manual-truck"]), 5).SpeedKph);
    }

    [Fact]
    public void Geometry_PredictsSharpAndGradualBends_HandlesDuplicatePoints()
    {
        var corner = new RouteSpeedProfile([new() { Lat = 30, Lng = 120 }, new() { Lat = 30.003, Lng = 120 }, new() { Lat = 30.003, Lng = 120.003 }]);
        var at = LocationSimulationService.Distance(new() { Lat = 30, Lng = 120 }, new() { Lat = 30.003, Lng = 120 });
        var options = new VehicleSimulationOptions();
        Assert.True(corner.SpeedLimit(at - 20, options).IsCurve);
        Assert.True(corner.SpeedLimit(at - 20, options).Kph < corner.SpeedLimit(at - 150, options).Kph);
        Assert.InRange(corner.SpeedLimit(at, options).Kph, 10, 30);
        var smooth = new RouteSpeedProfile([new() { Lat = 30, Lng = 120 }, new() { Lat = 30.0001, Lng = 120 }, new() { Lat = 30.0002, Lng = 120.000011 }]);
        Assert.True(smooth.SpeedLimit(10, options).IsCurve);
        var duplicate = new RouteSpeedProfile([new() { Lat = 30, Lng = 120 }, new() { Lat = 30, Lng = 120 }, new() { Lat = 30.001, Lng = 120 }]);
        Assert.Equal(30.001, duplicate.Sample(duplicate.TotalDistance).Point.Lat, 6);
        Assert.Throws<ArgumentException>(() => new RouteSpeedProfile([new() { Lat = double.NaN, Lng = 120 }, new() { Lat = 30, Lng = 120 }]));
    }

    [Fact]
    public async Task VehicleService_PositionUsesMeasuredSpeed_AndStopsAtExactEndpoint()
    {
        var path = new[] { new GeoPoint { Lat = 30, Lng = 120 }, new GeoPoint { Lat = 30.00002, Lng = 120 } };
        var updates = new List<(GeoPoint Point, VehicleTelemetry Telemetry)>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await new LocationSimulationService().RunVehicleAsync(path, new VehicleDriver(new VehicleSimulationOptions { Enabled = true }),
            (point, _, telemetry) => updates.Add((point, telemetry)), timeout.Token);
        Assert.True(updates.Count > 5);
        Assert.Equal(0, updates[0].Telemetry.TraveledMeters);
        Assert.Equal(path[^1].Lat, updates[^1].Point.Lat, 8);
        Assert.Equal(0, updates[^1].Telemetry.SpeedKph);
        Assert.Equal("finished", updates[^1].Telemetry.Phase);
        Assert.All(updates, item => Assert.Equal(item.Telemetry.TraveledMeters,
            LocationSimulationService.Distance(path[0], item.Point), 4));
        Assert.True(updates.Zip(updates.Skip(1)).All(pair => pair.First.Telemetry.TraveledMeters <= pair.Second.Telemetry.TraveledMeters));
    }

    [Fact]
    public async Task VehicleService_CancellationWorksWhileManualCarIsStationary()
    {
        var driver = new VehicleDriver(VehicleSimulationOptions.Presets["small-manual"]);
        driver.SetControls(new VehicleControls(false, 0, 0, 0));
        using var token = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocationSimulationService().RunVehicleAsync(
            [new() { Lat = 30, Lng = 120 }, new() { Lat = 31, Lng = 121 }], driver, (_, _, _) => { }, token.Token));
    }

    [Fact]
    public async Task Persistence_RoundTripsSettings_AndIsolatesOldAndNew()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cts-vehicle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var oldStore = new AtomicJsonConfigStore<VehicleSimulationOptions>(Path.Combine(directory, "old", "vehicle.json"));
            var newStore = new AtomicJsonConfigStore<VehicleSimulationOptions>(Path.Combine(directory, "new", "vehicle.json"));
            var options = (VehicleSimulationOptions.Presets["manual-truck"] with { Enabled = true, RandomEventsEnabled = true }).Validate();
            options.TrafficEvents.Events["signalQueue"] = options.TrafficEvents.Events["signalQueue"] with { ProbabilityPercent = 83, IntervalSeconds = 99 };
            await oldStore.SaveAsync(options);
            Assert.Equal("manual-truck", oldStore.Load().Validate().Preset);
            Assert.False(newStore.Load().Enabled);
            Assert.Equal(83, oldStore.Load().Validate().TrafficEvents.Events["signalQueue"].ProbabilityPercent);
            Assert.Equal(99, oldStore.Load().Validate().TrafficEvents.Events["signalQueue"].IntervalSeconds);
            Assert.NotEqual(83, newStore.Load().TrafficEvents.Events["signalQueue"].ProbabilityPercent);
            var webOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(options, webOptions);
            Assert.Equal(options.GearRatios, JsonSerializer.Deserialize<VehicleSimulationOptions>(json, webOptions)!.Validate().GearRatios);
            var controls = JsonSerializer.Deserialize<VehicleControls>("{\"automatic\":false,\"gear\":3,\"throttle\":0.6,\"brake\":0.2}", webOptions)!;
            Assert.False(controls.Automatic);
            Assert.Equal(3, controls.Gear);
        }
        finally { Directory.Delete(directory, true); }
    }
}
