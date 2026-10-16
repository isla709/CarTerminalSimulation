namespace TerminalSimulation.Wpf.Services;

internal sealed record RouteDrivingContext(double TraveledMeters, double TurnDistanceMeters = double.PositiveInfinity,
    double TurnAngle = 0, bool InBend = false);

internal readonly record struct VehicleEventInfluence(string Kind, string Stage, double RemainingSeconds,
    double CruiseMultiplier, double SpeedLimitKph, bool Hold, int Cycle = 0, int TotalCycles = 0)
{
    public static VehicleEventInfluence None => new("", "", 0, 1, double.PositiveInfinity, false);
}

// Synthetic traffic, never live signal/road observations. Phases influence the existing
// longitudinal driver; they never teleport a vehicle or change the saved route.
internal sealed class VehicleEventController
{
    private sealed record Phase(string Stage, double Seconds, double LimitKph, bool Stop = false,
        double TravelMeters = 0, int Cycle = 1);
    private readonly Random _random;
    private readonly Dictionary<string, double> _clocks = new();
    private readonly Queue<Phase> _phases = new();
    private Phase? _phase;
    private TrafficEventRule? _rule;
    private string _kind = "";
    private double _gap, _remaining, _targetAt, _elapsed;
    private double _lastSignalAt = double.NegativeInfinity;
    private bool _initialized, _stopped;
    private int _cycles;

    public VehicleEventController(Random random) => _random = random;
    private double Between(double min, double max) => min + _random.NextDouble() * (max - min);
    // Independent exponential opportunity clocks avoid all event types firing together.
    private double NextOpportunity(double mean) => -Math.Log(Math.Max(1e-9, 1 - Math.Clamp(_random.NextDouble(), 1e-6, 1 - 1e-9))) * mean;

    internal void Start(string kind, TrafficEventRule rule, double stopAt, int? cycles = null)
    {
        var definition = TrafficEventCatalog.All.FirstOrDefault(x => x.Id == kind)
            ?? throw new ArgumentException("未知交通事件", nameof(kind));
        _kind = kind; _rule = rule; _phase = null; _phases.Clear(); _elapsed = 0;
        _cycles = definition.HasCycles ? cycles ?? _random.Next(rule.MinCycles, rule.MaxCycles + 1) : 1;
        double Duration() => Between(rule.MinDurationSeconds, rule.MaxDurationSeconds);
        void Stop(string stage, int cycle = 1) => _phases.Enqueue(new(stage, Duration(), 0, Stop: true, Cycle: cycle));
        void Timed(string stage, double duration, double speed, int cycle = 1) => _phases.Enqueue(new(stage, duration, speed, Cycle: cycle));
        void Move(string stage, double meters, double speed, int cycle = 1) => _phases.Enqueue(new(stage, 0, speed, TravelMeters: meters, Cycle: cycle));
        void Reaction(int cycle = 1) => Timed("startDelay", Between(.8, 2.2), 0, cycle);
        switch (kind)
        {
            case "signalSingle":
                Stop("red"); Reaction(); Timed("releasing", 4, 22); break;
            case "signalQueue":
                for (var cycle = 1; cycle <= _cycles; cycle++)
                {
                    Stop("redQueue", cycle); Reaction(cycle);
                    if (cycle < _cycles) Move("queueMove", 16, rule.SlowSpeedKph, cycle);
                }
                Timed("releasing", 5, 22, _cycles); break;
            case "stopGo":
            case "mergeAlternating":
                for (var cycle = 1; cycle <= _cycles; cycle++)
                {
                    Stop(kind == "stopGo" ? "queueStop" : "mergeWait", cycle); Reaction(cycle);
                    if (cycle < _cycles) Move("queueMove", Between(12, 25), rule.SlowSpeedKph, cycle);
                }
                Timed("releasing", 6, rule.SlowSpeedKph + 8, _cycles); break;
            case "phantomJam":
                for (var cycle = 1; cycle <= _cycles; cycle++)
                {
                    // Some compression waves stop traffic completely; others only slow it.
                    var floor = _random.NextDouble() < .3 ? 0 : rule.SlowSpeedKph * Between(.55, 1);
                    Timed("compression", Duration(), floor, cycle);
                    Timed("waveRelease", Between(5, 10), rule.SlowSpeedKph + 16, cycle);
                }
                break;
            case "slowLeader": Timed("following", Duration(), rule.SlowSpeedKph); break;
            case "cutIn": Timed("gapOpening", Duration(), rule.SlowSpeedKph); Timed("recovering", 5, rule.SlowSpeedKph + 12); break;
            case "speedBump":
                _phases.Enqueue(new("bumpApproach", 0, rule.SlowSpeedKph, TravelMeters: 1));
                Timed("bumpPass", Duration(), rule.SlowSpeedKph); break;
            default:
                Stop(kind switch { "leftTurnYield" => "oncomingWait", "crosswalkYield" => "pedestrianWait",
                    "busObstruction" => "obstructionWait", "roadworks" => "workzoneWait",
                    "emergencyYield" => "emergencyWait", "railCrossing" => "barrierWait", _ => throw new ArgumentException("未知交通事件") });
                Reaction();
                Timed(kind == "roadworks" ? "narrowPass" : "cautiousPass", kind == "roadworks" ? Duration() : 5, rule.SlowSpeedKph);
                break;
        }
        _targetAt = stopAt;
    }

    internal VehicleEventInfluence Update(double seconds, RouteDrivingContext context, double speedKph,
        double routeRemaining, VehicleSimulationOptions options, bool automatic)
    {
        var settings = options.TrafficEvents;
        if (!_initialized)
        {
            _initialized = true; _gap = settings.MinimumGapSeconds;
            foreach (var (id, rule) in settings.Events) _clocks[id] = NextOpportunity(rule.IntervalSeconds);
        }
        if (!options.Enabled || !options.RandomEventsEnabled || !automatic)
        {
            if (_kind.Length > 0) Finish(settings);
            _gap = settings.MinimumGapSeconds;
            return VehicleEventInfluence.None;
        }
        if (_kind.Length == 0)
        {
            if (speedKph < 8 || routeRemaining < 100) return VehicleEventInfluence.None;
            if (_gap > 0) { _gap -= seconds; return VehicleEventInfluence.None; }
            Schedule(seconds, context, speedKph, routeRemaining, options);
            if (_kind.Length == 0) return VehicleEventInfluence.None;
        }
        if (!settings.Events.TryGetValue(_kind, out var currentRule) || !currentRule.Enabled || currentRule.ProbabilityPercent == 0)
        { Finish(settings); return VehicleEventInfluence.None; }
        if (_phase is null) EnterNext(context.TraveledMeters, first: true);
        var phase = _phase!;
        _elapsed += seconds;
        var distance = Math.Max(0, _targetAt - context.TraveledMeters);
        if (phase.Stop)
        {
            // Waiting only starts at the queue head after the car has actually stopped.
            if (distance <= .35 || (distance < 1.5 && speedKph < .5)) _stopped = true;
            if (_stopped)
            {
                if (speedKph < .2) _remaining = Math.Max(0, _remaining - seconds);
                if (_remaining <= 0) return Advance(context, speedKph, settings);
                return Influence(speedKph < .2 ? phase.Stage : "braking", 0, true);
            }
            var brake = Math.Min(options.BrakeDeceleration, options.DrivingStyle == "calm" ? 1.5 : 1.9);
            var cap = Math.Sqrt(2 * brake * Math.Max(0, distance - speedKph / 3.6 * 1.1)) * 3.6;
            return Influence("approaching", cap, false);
        }
        if (phase.TravelMeters > 0)
        {
            if (distance <= .35) return Advance(context, speedKph, settings);
            var cap = phase.Stage == "bumpApproach"
                ? Math.Sqrt(Math.Pow(_rule!.SlowSpeedKph / 3.6, 2) + 2 * Math.Min(options.BrakeDeceleration, 1.8) * Math.Max(0, distance - 9)) * 3.6
                : Math.Min(phase.LimitKph, Math.Sqrt(2 * Math.Min(options.BrakeDeceleration, 1.5) * Math.Max(0, distance + 6 - speedKph / 3.6 * 1.1)) * 3.6);
            return Influence(phase.Stage, cap, false);
        }
        // Reaction time is stationary time; waves and following use elapsed driving time.
        if (phase.LimitKph > 0 || speedKph < .2) _remaining = Math.Max(0, _remaining - seconds);
        if (_remaining <= 0) return Advance(context, speedKph, settings);
        var limit = phase.Stage == "following" ? phase.LimitKph * (1 + .09 * Math.Sin(_elapsed / 2.8) + .04 * Math.Sin(_elapsed / 6.1)) : phase.LimitKph;
        return Influence(phase.Stage, limit, phase.LimitKph == 0);
    }

    private VehicleEventInfluence Influence(string stage, double limit, bool hold) =>
        new(_kind, stage, _phase!.TravelMeters > 0 ? 0 : _remaining, 1, limit, hold, _phase.Cycle, _cycles);

    private VehicleEventInfluence Advance(RouteDrivingContext context, double speedKph, TrafficEventOptions settings)
    {
        if (_phases.Count == 0) { Finish(settings); return VehicleEventInfluence.None; }
        EnterNext(context.TraveledMeters, first: false);
        // Evaluate next phase on the next tick, without consuming the same time twice.
        return Influence(_phase!.Stop ? "approaching" : _phase.Stage,
            _phase.Stop ? Math.Min(speedKph, _rule!.SlowSpeedKph) : _phase.LimitKph, _phase.LimitKph == 0 && !_phase.Stop);
    }

    private void EnterNext(double traveled, bool first)
    {
        _phase = _phases.Dequeue(); _remaining = _phase.Seconds; _elapsed = 0; _stopped = false;
        if (_phase.Stop && !first) _targetAt = traveled + 6;
        else if (_phase.TravelMeters > 0)
            _targetAt = _phase.Stage == "bumpApproach" ? _targetAt + 9 : traveled + _phase.TravelMeters;
    }

    private void Schedule(double seconds, RouteDrivingContext context, double speedKph, double remaining, VehicleSimulationOptions options)
    {
        var settings = options.TrafficEvents;
        var brake = Math.Min(options.BrakeDeceleration, options.DrivingStyle == "calm" ? 1.5 : 1.9);
        // Reaction/jerk headroom, also for the less agile truck preset.
        var brakingDistance = Math.Pow(speedKph / 3.6, 2) / (2 * brake) + speedKph / 3.6 * 2.5 + 15;
        if (context.InBend || remaining < brakingDistance + 70) return;
        string? winner = null;
        double earliest = double.PositiveInfinity;
        foreach (var definition in TrafficEventCatalog.All)
        {
            if (!settings.Events.TryGetValue(definition.Id, out var rule) || !rule.Enabled || rule.ProbabilityPercent <= 0) continue;
            var signal = definition.Id is "signalSingle" or "signalQueue";
            var turnJunction = Math.Abs(context.TurnAngle) >= 40 && context.TurnDistanceMeters > brakingDistance + 35 && context.TurnDistanceMeters < brakingDistance + 260;
            var virtualJunction = settings.AllowVirtualSignalsOnStraight && context.TurnDistanceMeters > brakingDistance + 280 &&
                context.TraveledMeters + brakingDistance + 35 - _lastSignalAt >= settings.VirtualSignalSpacingMeters;
            if (signal && !(turnJunction || virtualJunction)) continue;
            if (signal && context.TraveledMeters + context.TurnDistanceMeters - _lastSignalAt < 80) continue;
            if (definition.Id == "leftTurnYield" && !(turnJunction && context.TurnAngle < -40)) continue;
            if (!signal && definition.Id != "leftTurnYield" && context.TurnDistanceMeters < brakingDistance + 45) continue;
            if (!_clocks.ContainsKey(definition.Id)) _clocks[definition.Id] = NextOpportunity(rule.IntervalSeconds);
            _clocks[definition.Id] -= seconds;
            if (_clocks[definition.Id] > 0) continue;
            var overdue = _clocks[definition.Id];
            _clocks[definition.Id] = NextOpportunity(rule.IntervalSeconds);
            if (_random.NextDouble() * 100 >= rule.ProbabilityPercent) continue;
            if (overdue < earliest) { earliest = overdue; winner = definition.Id; }
        }
        if (winner is null) return;
        var selected = settings.Events[winner];
        var cycles = _random.Next(selected.MinCycles, selected.MaxCycles + 1);
        var isSignal = winner is "signalSingle" or "signalQueue";
        var useTurn = (isSignal || winner == "leftTurnYield") && Math.Abs(context.TurnAngle) >= 40 && context.TurnDistanceMeters < brakingDistance + 260;
        var finalStop = context.TraveledMeters + (useTurn ? context.TurnDistanceMeters - 8 : brakingDistance + Between(25, 55));
        if (winner == "signalQueue")
        {
            if (!useTurn) finalStop += (cycles - 1) * 22;
            var fit = 1 + (int)Math.Floor((finalStop - context.TraveledMeters - brakingDistance) / 22);
            cycles = Math.Min(cycles, fit);
            if (cycles < selected.MinCycles) return;
        }
        var firstStop = finalStop - (winner == "signalQueue" ? (cycles - 1) * 22 : 0);
        var reserve = winner is "stopGo" or "mergeAlternating" ? cycles * 35 + 35 : 35;
        if (finalStop + reserve >= context.TraveledMeters + remaining) return;
        if (isSignal) _lastSignalAt = finalStop;
        Start(winner, selected, firstStop, cycles);
    }

    private void Finish(TrafficEventOptions settings)
    {
        _kind = ""; _phase = null; _phases.Clear(); _rule = null;
        _gap = settings.MinimumGapSeconds + Between(0, settings.MinimumGapSeconds * .5);
        // No backlog: a scenario due during another one never fires immediately afterward.
        foreach (var (id, rule) in settings.Events) _clocks[id] = NextOpportunity(rule.IntervalSeconds);
    }
}
