using System.Text.Json;
using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class TrafficEventTests
{
    private static VehicleSimulationOptions Options => (VehicleSimulationOptions.Presets["electric"] with
    { Enabled = true, RandomEventsEnabled = true, AverageSpeedKph = 50, MaxSpeedKph = 75 }).Validate();
    private static TrafficEventRule Quick(string id) => TrafficEventCatalog.All.Single(x => x.Id == id).Defaults with
    { Enabled = true, ProbabilityPercent = 100, IntervalSeconds = 5, MinDurationSeconds = 1, MaxDurationSeconds = 1, MinCycles = 2, MaxCycles = 2 };
    private static VehicleSimulationOptions Only(string id, double probability = 100, double interval = 5, bool straightSignals = true)
    {
        var events = TrafficEventCatalog.CreateDefaults();
        foreach (var key in events.Keys.ToArray()) events[key] = events[key] with { Enabled = key == id };
        events[id] = Quick(id) with { ProbabilityPercent = probability, IntervalSeconds = interval };
        return (Options with { TrafficEvents = new() { MinimumGapSeconds = 5, AllowVirtualSignalsOnStraight = straightSignals, Events = events } }).Validate();
    }
    private sealed class FixedRandom(double value = .01) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int minValue, int maxValue) => minValue;
    }
    private static VehicleEventInfluence Schedule(VehicleSimulationOptions options, RouteDrivingContext? context = null, double remaining = 5000, double seconds = 30)
    {
        var controller = new VehicleEventController(new FixedRandom());
        VehicleEventInfluence state = default;
        for (var i = 0; i < seconds * 20 && string.IsNullOrEmpty(state.Kind); i++)
            state = controller.Update(.05, context ?? new(100), 40, remaining, options, true);
        return state;
    }

    [Fact]
    public void Catalog_ReplacesLegacyScenarios_AndDefaultsAreValidatedAndCloned()
    {
        Assert.Equal(14, TrafficEventCatalog.All.Count);
        Assert.Equal(14, TrafficEventCatalog.All.Select(x => x.Id).Distinct().Count());
        Assert.DoesNotContain(TrafficEventCatalog.All, x => new[] { "mergeQueue", "mergeAccelerate", "redLight", "pedestrianYield", "overtake" }.Contains(x.Id));
        Assert.False(new VehicleSimulationOptions().RandomEventsEnabled);
        var source = Options;
        var copy = source.Validate();
        Assert.NotSame(source.TrafficEvents.Events, copy.TrafficEvents.Events);
        copy.TrafficEvents.Events["signalSingle"] = Quick("signalSingle");
        Assert.NotEqual(source.TrafficEvents.Events["signalSingle"], copy.TrafficEvents.Events["signalSingle"]);
        Assert.False(copy.TrafficEvents.Events["railCrossing"].Enabled);
    }

    [Theory]
    [InlineData("signalSingle")][InlineData("signalQueue")][InlineData("leftTurnYield")]
    [InlineData("crosswalkYield")][InlineData("stopGo")][InlineData("phantomJam")]
    [InlineData("slowLeader")][InlineData("cutIn")][InlineData("mergeAlternating")]
    [InlineData("busObstruction")][InlineData("roadworks")][InlineData("emergencyYield")]
    [InlineData("speedBump")][InlineData("railCrossing")]
    public void Scheduler_CanGenerateEveryConfiguredScenario(string id)
    {
        var context = id == "leftTurnYield" ? new RouteDrivingContext(100, 240, -90) : new RouteDrivingContext(100);
        Assert.Equal(id, Schedule(Only(id), context).Kind);
    }

    [Fact]
    public void Scheduler_RespectsProbabilityFrequencyEnabledMasterSwitchAndPlacement()
    {
        Assert.True(string.IsNullOrEmpty(Schedule(Only("stopGo", probability: 0)).Kind));
        Assert.True(string.IsNullOrEmpty(Schedule(Only("stopGo") with { RandomEventsEnabled = false }).Kind));
        var off = Only("stopGo"); off.TrafficEvents.Events["stopGo"] = Quick("stopGo") with { Enabled = false };
        Assert.True(string.IsNullOrEmpty(Schedule(off).Kind));
        Assert.True(string.IsNullOrEmpty(Schedule(Only("signalSingle", straightSignals: false)).Kind));
        Assert.Equal("signalSingle", Schedule(Only("signalSingle", straightSignals: false), new(0, 240, 90)).Kind);
        Assert.True(string.IsNullOrEmpty(Schedule(Only("leftTurnYield"), new(0, 240, 90)).Kind));
        Assert.True(string.IsNullOrEmpty(Schedule(Only("stopGo"), new(0, InBend: true)).Kind));
        Assert.True(string.IsNullOrEmpty(Schedule(Only("stopGo"), remaining: 80).Kind));
        // Same RNG: a longer opportunity interval really reduces frequency, not just display text.
        Assert.Equal("slowLeader", Schedule(Only("slowLeader", interval: 5), seconds: 10).Kind);
        Assert.True(string.IsNullOrEmpty(Schedule(Only("slowLeader", interval: 3600), seconds: 10).Kind));
        var controller = new VehicleEventController(new FixedRandom(.5));
        var lowProbability = Only("slowLeader", probability: 40);
        for (var i = 0; i < 10000; i++) Assert.Equal("", controller.Update(.05, new(0), 40, 5000, lowProbability, true).Kind);
    }

    [Fact]
    public void Waiting_OnlyCountsAfterStop_ThenIncludesReactionAndRelease()
    {
        var controller = new VehicleEventController(new Random(1));
        controller.Start("signalSingle", Quick("signalSingle") with { MinDurationSeconds = 3, MaxDurationSeconds = 3 }, 100);
        var approaching = controller.Update(1, new(50), 30, 1000, Options, true);
        Assert.Equal(3, approaching.RemainingSeconds); Assert.Equal("approaching", approaching.Stage);
        var braking = controller.Update(1, new(100), 5, 1000, Options, true);
        Assert.True(braking.Hold); Assert.Equal(3, braking.RemainingSeconds);
        var stopped = controller.Update(1, new(100), 0, 1000, Options, true);
        Assert.Equal("red", stopped.Stage); Assert.Equal(2, stopped.RemainingSeconds);
        Assert.Equal("startDelay", controller.Update(2, new(100), 0, 1000, Options, true).Stage);
        var moving = controller.Update(3, new(100), 0, 1000, Options, true);
        Assert.Equal("releasing", moving.Stage); Assert.False(moving.Hold);
        Assert.Equal("", controller.Update(5, new(100), 10, 1000, Options, true).Kind);
        for (var i = 0; i < 100; i++) Assert.Equal("", controller.Update(.05, new(100), 40, 1000, Options, true).Kind);
    }

    private static List<VehicleTelemetry> Drive(string id, string preset = "electric")
    {
        var options = (VehicleSimulationOptions.Presets[preset] with
        { Enabled = true, RandomEventsEnabled = true, MaxSpeedKph = 75, AverageSpeedKph = 50,
            TrafficEvents = Only(id).TrafficEvents with { MinimumGapSeconds = 600 } }).Validate();
        var driver = new VehicleDriver(options, new Random(7));
        double traveled = 0, previous = 0;
        var readings = new List<VehicleTelemetry>();
        for (var i = 0; i < 1000; i++)
        {
            var state = driver.Tick(.05, 75, 10000 - traveled, context: new(traveled));
            traveled += (previous + state.SpeedKph) / 2 / 3.6 * .05; previous = state.SpeedKph;
        }
        // Replace any randomly selected event with a deterministic short scenario.
        driver.Events.Start(id, Quick(id), traveled + 120);
        for (var i = 0; i < 16000; i++)
        {
            var state = driver.Tick(.05, 75, 10000 - traveled, context: new(traveled));
            traveled += (previous + state.SpeedKph) / 2 / 3.6 * .05; previous = state.SpeedKph;
            readings.Add(state with { TraveledMeters = traveled });
            if (state.DrivingEvent.Length == 0) break;
        }
        return readings;
    }

    [Theory]
    [InlineData("signalSingle")][InlineData("signalQueue")][InlineData("leftTurnYield")]
    [InlineData("crosswalkYield")][InlineData("stopGo")][InlineData("phantomJam")]
    [InlineData("slowLeader")][InlineData("cutIn")][InlineData("mergeAlternating")]
    [InlineData("busObstruction")][InlineData("roadworks")][InlineData("emergencyYield")]
    [InlineData("speedBump")][InlineData("railCrossing")]
    public void ActualDriver_AllScenariosFinishWithoutDeadlocksAndRespectLimits(string id)
    {
        var states = Drive(id);
        Assert.Equal("", states[^1].DrivingEvent);
        Assert.All(states, x => { Assert.InRange(x.SpeedKph, 0, 75); Assert.InRange(x.TargetSpeedKph, 0, 75); Assert.True(double.IsFinite(x.Acceleration)); });
        if (id is not ("phantomJam" or "slowLeader" or "cutIn" or "speedBump"))
        {
            Assert.Contains(states, x => x.SpeedKph == 0 && x.Phase == "waiting");
            Assert.Contains(states, x => x.EventStage == "startDelay");
        }
        if (id is "signalQueue" or "stopGo" or "mergeAlternating")
        {
            Assert.Contains(states, x => x.EventCycle == 2 && x.SpeedKph == 0 && x.Phase == "waiting");
            Assert.Contains(states, x => x.EventStage == "queueMove" && x.SpeedKph > 3);
            Assert.All(states.Where(x => x.DrivingEvent.Length > 0), x => Assert.Equal(2, x.EventTotalCycles));
        }
        if (id == "phantomJam")
        {
            Assert.Contains(states, x => x.EventStage == "compression"); Assert.Contains(states, x => x.EventStage == "waveRelease");
            Assert.Contains(states, x => x.EventCycle == 2);
        }
    }

    [Fact]
    public void Truck_HandlesRepeatedQueueStartsAndStops() => Assert.Equal("", Drive("stopGo", "manual-truck")[^1].DrivingEvent);

    [Fact]
    public void ManualOperationCancelsScenarioAndResumingAutoDoesNotResumeOldStop()
    {
        var options = Options with { Transmission = "manual", GearRatios = [3.6, 2, 1] };
        var driver = new VehicleDriver(options, new Random(1));
        driver.Events.Start("signalQueue", Quick("signalQueue"), 0);
        Assert.Equal("waiting", driver.Tick(.05, 75, 1000, context: new(0)).Phase);
        driver.SetControls(new(false, 1, 1, 0));
        var manual = driver.Tick(.05, 75, 1000, context: new(0));
        Assert.Equal("", manual.DrivingEvent); Assert.Equal(1, manual.Throttle); Assert.Equal("manual", manual.ControlMode);
        driver.SetControls(new());
        Assert.Equal("", driver.Tick(.05, 75, 1000, context: new(0)).DrivingEvent);
    }

    [Fact]
    public void Settings_LegacyJsonGetsDefaults_NestedOverridesRoundTripAndRemainIsolated()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var legacy = JsonSerializer.Deserialize<VehicleSimulationOptions>("{\"enabled\":true,\"randomEventsEnabled\":true}", json)!.Validate();
        Assert.Equal(14, legacy.TrafficEvents.Events.Count); Assert.True(legacy.RandomEventsEnabled);
        var original = Only("signalQueue", probability: 73, interval: 123);
        var restored = JsonSerializer.Deserialize<VehicleSimulationOptions>(JsonSerializer.Serialize(original, json), json)!.Validate();
        Assert.Equal(original.TrafficEvents.Events["signalQueue"], restored.TrafficEvents.Events["signalQueue"]);
        restored.TrafficEvents.Events["signalQueue"] = Quick("signalQueue");
        Assert.Equal(73, original.TrafficEvents.Events["signalQueue"].ProbabilityPercent);
        Assert.Equal(14, (new TrafficEventOptions { Events = new() { ["cutIn"] = Quick("cutIn") } }).Validate().Events.Count);
    }

    [Theory]
    [InlineData("probability")][InlineData("interval")][InlineData("duration")][InlineData("cycles")][InlineData("speed")]
    public void Settings_RejectInvalidValues(string field)
    {
        var settings = new TrafficEventOptions();
        var rule = Quick("stopGo");
        settings.Events["stopGo"] = field switch
        {
            "probability" => rule with { ProbabilityPercent = double.NaN },
            "interval" => rule with { IntervalSeconds = 0 },
            "duration" => rule with { MinDurationSeconds = 20, MaxDurationSeconds = 10 },
            "cycles" => rule with { MinCycles = 4, MaxCycles = 2 },
            _ => rule with { SlowSpeedKph = 200 }
        };
        Assert.Throws<ArgumentException>(() => (Options with { TrafficEvents = settings }).Validate());
    }

    [Fact]
    public void CongestionWave_CanStopTraffic_AndCountdownDoesNotRunWhileStillMoving()
    {
        var controller = new VehicleEventController(new FixedRandom());
        controller.Start("phantomJam", Quick("phantomJam") with { MinDurationSeconds = 4, MaxDurationSeconds = 4 }, 0);
        var braking = controller.Update(2, new(0), 30, 5000, Options, true);
        Assert.True(braking.Hold); Assert.Equal(4, braking.RemainingSeconds);
        var stopped = controller.Update(1, new(5), 0, 5000, Options, true);
        Assert.Equal(3, stopped.RemainingSeconds); Assert.Equal("compression", stopped.Stage);
        var releasing = controller.Update(3, new(5), 0, 5000, Options, true);
        Assert.Equal("waveRelease", releasing.Stage); Assert.False(releasing.Hold);
    }

    [Fact]
    public void ActiveEvent_RespectsRouteAndEndpointEnvelope_AndDisabledTypeCancels()
    {
        var driver = new VehicleDriver(Options);
        driver.Events.Start("slowLeader", Quick("slowLeader") with { SlowSpeedKph = 45 }, 0);
        Assert.Equal(18, driver.Tick(.05, 18, 1000, context: new(0)).TargetSpeedKph);
        Assert.True(driver.Tick(.05, 75, .3, context: new(0)).TargetSpeedKph < 5);
        var controller = new VehicleEventController(new Random(1));
        controller.Start("signalSingle", Quick("signalSingle"), 0);
        var disabled = Options;
        disabled.TrafficEvents.Events["signalSingle"] = Quick("signalSingle") with { Enabled = false };
        Assert.Equal("", controller.Update(.05, new(0), 0, 1000, disabled, true).Kind);
    }

    [Fact]
    public async Task RouteLibrary_RoundTripsTrafficSettingsWithRawCoordinateSystem()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cts-traffic-route-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MapHistoryStore(Path.Combine(directory, "new", "history.json"));
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var route = System.Text.Json.Nodes.JsonNode.Parse("""{"mode":"draw","coordinateSystem":"gcj02","points":[{"lat":30,"lng":120},{"lat":30.01,"lng":120}]}""")!.AsObject();
            route["vehicle"] = JsonSerializer.SerializeToNode(Only("signalQueue", 83, 99), json);
            await store.AddRouteAsync("通勤排队", route);
            var saved = Assert.Single((await new MapHistoryStore(Path.Combine(directory, "new", "history.json")).ReadAsync()).Routes);
            Assert.Equal("gcj02", saved.Route["coordinateSystem"]!.ToString());
            var restored = saved.Route["vehicle"]!.Deserialize<VehicleSimulationOptions>(json)!.Validate();
            Assert.Equal(83, restored.TrafficEvents.Events["signalQueue"].ProbabilityPercent);
            Assert.Equal(99, restored.TrafficEvents.Events["signalQueue"].IntervalSeconds);
            Assert.Empty((await new MapHistoryStore(Path.Combine(directory, "old", "history.json")).ReadAsync()).Routes);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void MultiCycleSignal_QueueStopsRemainBeforeActualTurn_AndRouteContinues()
    {
        var points = new TerminalSimulation.Wpf.ViewModels.GeoPoint[]
        { new() { Lat = 30, Lng = 120 }, new() { Lat = 30.005, Lng = 120 }, new() { Lat = 30.005, Lng = 120.005 } };
        var geometry = new RouteSpeedProfile(points);
        var junction = LocationSimulationService.Distance(points[0], points[1]);
        var driver = new VehicleDriver(Only("signalQueue", straightSignals: false), new FixedRandom());
        double traveled = 0, previous = 0;
        var waits = new List<(double At, int Cycle)>();
        for (var i = 0; i < 16000 && traveled < junction + 40; i++)
        {
            var limit = geometry.SpeedLimit(traveled, driver.Options);
            var state = driver.Tick(.05, limit.Kph, geometry.TotalDistance - traveled, limit.Constraint, geometry.DrivingContext(traveled));
            if (state.DrivingEvent == "signalQueue" && state.EventStage == "redQueue" && state.SpeedKph < .2)
                waits.Add((traveled, state.EventCycle));
            traveled += (previous + state.SpeedKph) / 2 / 3.6 * .05; previous = state.SpeedKph;
        }
        Assert.Contains(waits, x => x.Cycle == 1); Assert.Contains(waits, x => x.Cycle == 2);
        Assert.All(waits, x => Assert.True(x.At < junction - 2, $"Queue stop passed junction: {x.At}/{junction}"));
        Assert.True(waits.Where(x => x.Cycle == 2).First().At > waits[0].At + 10);
        Assert.True(traveled >= junction + 40);
    }

    [Fact]
    public void ConfigValidation_RejectsNullUnknownAndInvalidGlobalOptions()
    {
        Assert.Throws<ArgumentException>(() => (Options with { TrafficEvents = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (new TrafficEventOptions { MinimumGapSeconds = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (new TrafficEventOptions { VirtualSignalSpacingMeters = double.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentException>(() => (new TrafficEventOptions { Events = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (new TrafficEventOptions { Events = new() { ["unknown"] = new() } }).Validate());
    }

    [Fact]
    public async Task Simulation_CanBeCancelledDuringSignalWaiting()
    {
        var driver = new VehicleDriver(Options);
        driver.Events.Start("signalSingle", Quick("signalSingle") with { MinDurationSeconds = 180, MaxDurationSeconds = 180 }, 0);
        using var token = new CancellationTokenSource();
        var updates = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocationSimulationService().RunVehicleAsync(
            [new() { Lat = 30, Lng = 120 }, new() { Lat = 30.01, Lng = 120 }], driver,
            (point, _, telemetry) =>
            {
                Assert.Equal(0, telemetry.SpeedKph); Assert.Equal("signalSingle", telemetry.DrivingEvent);
                Assert.Equal(30, point.Lat); if (++updates == 2) token.Cancel();
            }, token.Token));
        Assert.Equal(2, updates);
    }
}
