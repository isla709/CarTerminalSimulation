namespace TerminalSimulation.Wpf.Services;

internal sealed record RouteDrivingContext(double TraveledMeters, double TurnDistanceMeters = double.PositiveInfinity,
    double TurnAngle = 0, bool InBend = false);

internal readonly record struct VehicleEventInfluence(string Kind, string Stage, double RemainingSeconds,
    double CruiseMultiplier, double SpeedLimitKph, bool Hold)
{
    public static VehicleEventInfluence None => new("", "", 0, 1, double.PositiveInfinity, false);
}

// Synthetic longitudinal scenarios, not observations of actual traffic/signals.
// One event at a time; manual controls always take precedence.
internal sealed class VehicleEventController
{
    private readonly Random _random;
    private double _untilNext, _remaining, _stopAt, _boost, _slowSpeed;
    private string _kind = "";
    private bool _waiting;
    public VehicleEventController(Random random) { _random = random; _untilNext = Between(15, 35); }
    private double Between(double min, double max) => min + _random.NextDouble() * (max - min);

    internal void Start(string kind, double duration, double stopAt, double boost = 1.25, double slowSpeed = 10)
    {
        if (kind is not ("mergeQueue" or "mergeAccelerate" or "pedestrianYield" or "overtake" or "redLight"))
            throw new ArgumentException("未知驾驶事件", nameof(kind));
        _kind = kind; _remaining = duration; _stopAt = stopAt; _boost = boost; _slowSpeed = slowSpeed; _waiting = false;
    }

    internal VehicleEventInfluence Update(double seconds, RouteDrivingContext context, double speedKph,
        double routeRemaining, VehicleSimulationOptions options, bool automatic)
    {
        if (!options.Enabled || !options.RandomEventsEnabled || !automatic)
        {
            if (_kind.Length > 0) Finish();
            return VehicleEventInfluence.None;
        }
        var began = false;
        if (_kind.Length == 0)
        {
            if (speedKph < 10 || routeRemaining < 80) return VehicleEventInfluence.None;
            _untilNext -= seconds;
            if (_untilNext > 0) return VehicleEventInfluence.None;
            var brakingDistance = speedKph / 3.6 * 1.3 + Math.Pow(speedKph / 3.6, 2) / (2 * Math.Min(options.BrakeDeceleration, 2));
            var junction = Math.Abs(context.TurnAngle) >= 45 && context.TurnDistanceMeters > brakingDistance + 5 && context.TurnDistanceMeters < brakingDistance + 90;
            var choices = new List<string> { "mergeQueue" };
            // Boosts are speed changes only; they never move the saved path sideways.
            if (!context.InBend && context.TurnDistanceMeters > brakingDistance + 100)
                choices.AddRange(["mergeAccelerate", "overtake"]);
            if (junction) choices.Add("redLight");
            if (junction && context.TurnAngle > 0 && context.TurnAngle < 150) choices.Add("pedestrianYield");
            var kind = choices[_random.Next(choices.Count)];
            var stopAt = kind is "redLight" or "pedestrianYield"
                ? context.TraveledMeters + context.TurnDistanceMeters - 5
                : context.TraveledMeters + Math.Max(35, brakingDistance + Between(15, 35));
            // Do not create a waiting point beyond the route endpoint.
            if (stopAt - context.TraveledMeters >= routeRemaining - 15) { _untilNext = 15; return VehicleEventInfluence.None; }
            Start(kind, kind == "redLight" ? Between(10, 30) : kind == "mergeQueue" ? Between(3, 9) : Between(4, 10),
                stopAt, Between(1.15, 1.4), Between(8, 15));
            began = true;
        }
        if (_kind is "mergeAccelerate" or "overtake")
        {
            if (!began) _remaining -= seconds;
            if (_remaining <= 0) { Finish(); return VehicleEventInfluence.None; }
            return new(_kind, "accelerating", _remaining, _boost, double.PositiveInfinity, false);
        }

        var distance = Math.Max(0, _stopAt - context.TraveledMeters);
        if (distance <= .3 || (distance < 1 && speedKph < .35)) _waiting = true;
        if (_waiting)
        {
            // Waiting time begins only after the vehicle has actually stopped.
            if (speedKph < .2) _remaining -= seconds;
            if (_remaining <= 0) { Finish(); return VehicleEventInfluence.None; }
            return new(_kind, speedKph < .2 ? "waiting" : "braking", _remaining, 1, 0, true);
        }
        var brake = Math.Min(options.BrakeDeceleration, 2);
        // Include controller reaction distance, so stopping happens before the point.
        var limit = Math.Sqrt(2 * brake * Math.Max(0, distance - speedKph / 3.6 * .8)) * 3.6;
        if (_kind == "mergeQueue") limit = Math.Min(limit, _slowSpeed);
        return new(_kind, _kind == "mergeQueue" && distance > 12 ? "queuing" : "braking", _remaining, 1, limit, false);
    }

    private void Finish() { _kind = ""; _waiting = false; _untilNext = Between(25, 65); }
}
