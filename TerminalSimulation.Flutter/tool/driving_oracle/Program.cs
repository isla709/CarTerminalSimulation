using System.Text.Json;
using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.ViewModels;

var drivers = new List<object>();
foreach (var preset in new[] { "small-auto", "small-manual", "electric", "manual-truck", "cvt-hybrid", "dct" })
{
    var options = (preset == "cvt-hybrid" ? new VehicleSimulationOptions { Transmission = "cvt", Powertrain = "hybrid" }
        : preset == "dct" ? new VehicleSimulationOptions { Transmission = "dct" }
        : VehicleSimulationOptions.Presets[preset]) with { Enabled = true };
    var driver = new VehicleDriver(options, new FixedRandom());
    var samples = new List<object>();
    for (var tick = 0; tick < 2600; tick++)
    {
        if (preset == "small-manual")
        {
            if (tick == 1400) driver.SetControls(new(false, 1, 1, 0));
            if (tick == 1500) driver.SetControls(new(false, 0, 1, 0));
            if (tick == 1600) driver.SetControls(new(false, 1, .8, 0));
            if (tick == 1750) driver.SetControls(new(false, 1, 0, 1));
            if (tick == 1850) driver.SetControls(new());
        }
        var limit = tick >= 800 && tick < 1100 ? 18 : 300;
        var state = driver.Tick(.05, limit, tick >= 2300 ? .1 : 10000);
        if (tick % 25 == 0 || new[] { 1399, 1400, 1499, 1500, 1599, 1600, 1749, 1750, 1849, 1850, 2599 }.Contains(tick))
            samples.Add(new { tick, state });
    }
    drivers.Add(new { preset, options, samples });
}
var events = new List<object>();
foreach (var definition in TrafficEventCatalog.All)
{
    var rule = definition.Defaults with { Enabled = true, ProbabilityPercent = 100, IntervalSeconds = 5,
        MinDurationSeconds = 1, MaxDurationSeconds = 1, MinCycles = 2, MaxCycles = 2 };
    var settings = new TrafficEventOptions { MinimumGapSeconds = 600 };
    settings.Events[definition.Id] = rule;
    var options = (VehicleSimulationOptions.Presets["electric"] with { Enabled = true, RandomEventsEnabled = true,
        MaxSpeedKph = 75, AverageSpeedKph = 50, TrafficEvents = settings }).Validate();
    var driver = new VehicleDriver(options, new FixedRandom());
    double traveled = 0, previous = 0;
    for (var tick = 0; tick < 1000; tick++)
    {
        var state = driver.Tick(.05, 75, 10000 - traveled, context: new(traveled));
        traveled += (previous + state.SpeedKph) / 2 / 3.6 * .05; previous = state.SpeedKph;
    }
    driver.Events.Start(definition.Id, rule, traveled + 120);
    var samples = new List<object>();
    var stages = new List<string>();
    var lastStage = "";
    int finalTick = -1;
    for (var tick = 0; tick < 16000; tick++)
    {
        var state = driver.Tick(.05, 75, 10000 - traveled, context: new(traveled));
        traveled += (previous + state.SpeedKph) / 2 / 3.6 * .05; previous = state.SpeedKph;
        var stage = state.EventStage + ":" + state.EventCycle;
        if (stage != lastStage) stages.Add(stage);
        if (tick < 3 || tick % 100 == 0 || stage != lastStage || state.DrivingEvent.Length == 0)
            samples.Add(new { tick, state, traveled });
        lastStage = stage;
        if (state.DrivingEvent.Length == 0) { finalTick = tick; break; }
    }
    if (finalTick < 0) throw new Exception("Reference scenario did not finish: " + definition.Id);
    events.Add(new { id = definition.Id, rule, options, samples, stages, finalTick });
}
var profiles = new List<object>();
var paths = new Dictionary<string, GeoPoint[]>
{
    ["right"] = [new() { Lat=30, Lng=120 }, new() { Lat=30.005, Lng=120 }, new() { Lat=30.005, Lng=120.005 }],
    ["left"] = [new() { Lat=30, Lng=120 }, new() { Lat=30.005, Lng=120 }, new() { Lat=30.005, Lng=119.995 }],
    ["duplicates"] = [new() { Lat=30, Lng=120 }, new() { Lat=30, Lng=120 }, new() { Lat=30.001, Lng=120 }]
};
GeoPoint Point(double east, double north) => new() { Lat=30+north/111195, Lng=120+east/(111195*Math.Cos(Math.PI/6)) };
var dense = new List<GeoPoint> { Point(0, -150) };
for (var degree=0; degree<=90; degree+=10) { var r=degree*Math.PI/180; dense.Add(Point(50*(1-Math.Cos(r)), 50*Math.Sin(r))); }
dense.Add(Point(200,50)); paths["dense"] = dense.ToArray();
foreach (var (name, path) in paths)
{
    var profile = new RouteSpeedProfile(path);
    var samples = new List<object>();
    foreach (var at in Enumerable.Range(0, 101).Select(i => profile.TotalDistance*i/100))
    {
        var limit = profile.SpeedLimit(at, VehicleSimulationOptions.Presets["electric"]);
        var context = profile.DrivingContext(at); var position = profile.Sample(at);
        samples.Add(new { at, kph=limit.Kph, isCurve=limit.IsCurve, constraint=limit.Constraint,
            turnDistanceMeters=double.IsFinite(context.TurnDistanceMeters)?context.TurnDistanceMeters:(double?)null,
            context.TurnAngle, context.InBend, point=position.Point, bearing=position.Bearing });
    }
    profiles.Add(new { name, path, profile.TotalDistance, samples });
}
var result = new { source="Original CTS C# source linked directly, fixed random stream 0.01 / integer minimum",
    presets=VehicleSimulationOptions.Presets, catalog=TrafficEventCatalog.All, drivers, events, profiles };
var target = args.FirstOrDefault() ?? "test/fixtures/driving-csharp.json";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
File.WriteAllText(target, JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Wrote {drivers.Count} drivers, {events.Count} scenarios, {profiles.Count} profiles");

sealed class FixedRandom : Random
{
    public override double NextDouble() => .01;
    public override int Next(int minValue, int maxValue) => minValue;
}
