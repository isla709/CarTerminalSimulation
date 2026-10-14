namespace TerminalSimulation.Wpf.Services;

internal sealed class LocationSimulationService : ILocationSimulationService
{
    public async Task RunAsync(IReadOnlyList<ViewModels.GeoPoint> path, Func<double> speedKph,
        Action<ViewModels.GeoPoint, int> positionChanged, CancellationToken cancellationToken)
    {
        if (path.Count < 2) throw new ArgumentException("路径至少需要两个点", nameof(path));
        await RunCoreAsync(path, speedKph, null, (point, bearing, _) => positionChanged(point, bearing), cancellationToken);
    }

    public Task RunVehicleAsync(IReadOnlyList<ViewModels.GeoPoint> path, VehicleDriver driver,
        Action<ViewModels.GeoPoint, int, VehicleTelemetry> changed, CancellationToken token) =>
        RunCoreAsync(path, () => 0, driver, (point, bearing, telemetry) => changed(point, bearing, telemetry!), token);

    private static async Task RunCoreAsync(IReadOnlyList<ViewModels.GeoPoint> path, Func<double> fixedSpeed,
        VehicleDriver? driver, Action<ViewModels.GeoPoint, int, VehicleTelemetry?> changed, CancellationToken token)
    {
        var geometry = new RouteSpeedProfile(path);
        var traveled = 0d;
        var elapsed = 0d;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastTime = clock.Elapsed.TotalSeconds;
        VehicleTelemetry? telemetry = driver is null ? null : driver.Tick(.001, driver.Options.MaxSpeedKph, geometry.TotalDistance);
        var first = geometry.Sample(0);
        changed(first.Point, first.Bearing, telemetry);
        while (traveled < geometry.TotalDistance)
        {
            await Task.Delay(100, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var now = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(now - lastTime, .001, 1); // do not teleport after a suspended process
            lastTime = now;
            var steps = Math.Max(1, (int)Math.Ceiling(dt / .05));
            var stepTime = dt / steps;
            for (var i = 0; i < steps && traveled < geometry.TotalDistance; i++)
            {
                elapsed += stepTime;
                var before = telemetry?.SpeedKph ?? Math.Max(0, fixedSpeed());
                if (driver is not null)
                {
                    var limit = geometry.SpeedLimit(traveled, driver.Options);
                    telemetry = driver.Tick(stepTime, limit.Kph, geometry.TotalDistance - traveled, limit.Constraint, geometry.DrivingContext(traveled));
                }
                var after = telemetry?.SpeedKph ?? before;
                traveled = Math.Min(geometry.TotalDistance, traveled + (before + after) / 2 / 3.6 * stepTime);
                if (driver is not null && geometry.TotalDistance - traveled < .15 && after < .5) traveled = geometry.TotalDistance;
            }
            var position = geometry.Sample(traveled);
            if (telemetry is not null) telemetry = telemetry with
            {
                TraveledMeters = traveled, RemainingMeters = Math.Max(0, geometry.TotalDistance - traveled),
                ActualAverageSpeedKph = elapsed > 0 ? traveled / elapsed * 3.6 : 0
            };
            changed(position.Point, position.Bearing, telemetry);
        }
        var end = geometry.Sample(geometry.TotalDistance);
        changed(end.Point, end.Bearing, telemetry is null ? null : telemetry with { SpeedKph = 0, Acceleration = 0, Throttle = 0, Brake = 0, Phase = "finished", RemainingMeters = 0 });
    }

    internal static double Distance(ViewModels.GeoPoint from, ViewModels.GeoPoint to)
    {
        const double earthRadius = 6371e3;
        var phi1 = from.Lat * Math.PI / 180;
        var phi2 = to.Lat * Math.PI / 180;
        var deltaPhi = (to.Lat - from.Lat) * Math.PI / 180;
        var deltaLambda = (to.Lng - from.Lng) * Math.PI / 180;
        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        a = Math.Clamp(a, 0, 1);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    internal static int Bearing(ViewModels.GeoPoint from, ViewModels.GeoPoint to)
        => (int)Math.Round(BearingDegrees(from, to)) % 360;

    internal static double BearingDegrees(ViewModels.GeoPoint from, ViewModels.GeoPoint to)
    {
        var phi1 = from.Lat * Math.PI / 180;
        var phi2 = to.Lat * Math.PI / 180;
        var deltaLambda = (to.Lng - from.Lng) * Math.PI / 180;
        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }
}

internal sealed class RouteSpeedProfile
{
    private readonly ViewModels.GeoPoint[] _points;
    private readonly double[] _offsets;
    private sealed record Bend(double At, double Radius, double Angle, double ExitDistance, double LastAt);
    private readonly Bend[] _curves;
    private readonly double[] _curveOffsets;
    public double TotalDistance => _offsets[^1];

    public RouteSpeedProfile(IReadOnlyList<ViewModels.GeoPoint> path)
    {
        if (path.Count < 2 || path.Count > 100_000) throw new ArgumentException("路径点数量必须为 2–100000");
        _points = path.Select(point => new ViewModels.GeoPoint { Lat = point.Lat, Lng = point.Lng }).ToArray();
        if (_points.Any(point => !double.IsFinite(point.Lat) || !double.IsFinite(point.Lng) || point.Lat is < -90 or > 90 || point.Lng is < -180 or > 180)) throw new ArgumentException("路径含无效坐标");
        _offsets = new double[_points.Length];
        for (var i = 1; i < _points.Length; i++) _offsets[i] = _offsets[i - 1] + LocationSimulationService.Distance(_points[i - 1], _points[i]);
        var curves = new List<Bend>();
        for (var i = 1; i < _points.Length - 1; i++)
        {
            var before = _offsets[i] - _offsets[i - 1];
            var after = _offsets[i + 1] - _offsets[i];
            if (before < 1 || after < 1) continue;
            var signed = (LocationSimulationService.BearingDegrees(_points[i], _points[i + 1]) - LocationSimulationService.BearingDegrees(_points[i - 1], _points[i]) + 540) % 360 - 180;
            var delta = Math.Abs(signed);
            if (delta < 2) continue;
            // Approximate bend radius from adjacent route geometry. Long freehand
            // segments are capped so their corner does not imply a huge radius.
            var radius = Math.Max(5, Math.Min(35, (before + after) / 2) / (2 * Math.Sin(delta * Math.PI / 360)));
            var exit = Math.Clamp(radius * .7, 12, 40);
            // Adjacent small bends in one direction form one continuous turn,
            // rather than repeated braking at every navigation polyline vertex.
            if (curves.Count > 0 && Math.Sign(curves[^1].Angle) == Math.Sign(signed) &&
                Math.Abs(curves[^1].Angle + signed) < 175 && delta < 45 && _offsets[i] - curves[^1].LastAt <= 35)
            {
                var previous = curves[^1];
                var weight = Math.Abs(previous.Angle) + delta;
                var apex = (previous.At * Math.Abs(previous.Angle) + _offsets[i] * delta) / weight;
                curves[^1] = new Bend(apex, Math.Min(previous.Radius, radius), previous.Angle + signed, _offsets[i] - apex + exit, _offsets[i]);
            }
            else curves.Add(new Bend(_offsets[i], radius, signed, exit, _offsets[i]));
        }
        _curves = curves.ToArray();
        _curveOffsets = _curves.Select(curve => curve.At).ToArray();
    }

    internal (double Kph, bool IsCurve, string Constraint) SpeedLimit(double traveled, VehicleSimulationOptions options)
    {
        var style = options.DrivingStyle == "calm" ? .8 : options.DrivingStyle == "sport" ? 1.2 : 1;
        var lateral = (options.Powertrain == "diesel" && options.MaxAcceleration < 1 ? 1.1 : 2) * style;
        var brake = Math.Min(options.BrakeDeceleration, 2.2 * style);
        var limit = options.MaxSpeedKph / 3.6;
        var constraint = "cruise";
        var index = Array.BinarySearch(_curveOffsets, traveled);
        if (index < 0) index = Math.Max(0, ~index - 1);
        for (; index < _curves.Length; index++)
        {
            var curve = _curves[index];
            if (curve.At - traveled > 350) break;
            if (traveled > curve.At + curve.ExitDistance) continue;
            var corner = Math.Max(2, Math.Sqrt(lateral * curve.Radius));
            // Tight right-angle turns need lower apex speed than a sweeping bend.
            if (Math.Abs(curve.Angle) >= 65) corner = Math.Min(corner, (curve.Angle > 0 ? 20 : 25) / 3.6 * Math.Sqrt(style));
            double candidate;
            string phase;
            if (traveled < curve.At)
            {
                var reaction = Math.Max(5, options.AverageSpeedKph / 3.6 * 1.1);
                candidate = Math.Sqrt(corner * corner + 2 * brake * Math.Max(0, curve.At - traveled - reaction));
                phase = curve.Angle > 0 ? "turnRight" : "turnLeft";
            }
            else
            {
                // Release the brake at the apex, then progressively build speed.
                var exitAcceleration = Math.Min(options.MaxAcceleration * .65, 1.5 * style);
                candidate = Math.Sqrt(corner * corner + 2 * exitAcceleration * (traveled - curve.At + Math.Min(4, curve.Radius * .15)));
                phase = "turnExit";
            }
            if (candidate < limit) { limit = candidate; constraint = phase; }
        }
        return (limit * 3.6, limit * 3.6 < options.MaxSpeedKph - .1, constraint);
    }

    internal RouteDrivingContext DrivingContext(double traveled)
    {
        var index = Array.BinarySearch(_curveOffsets, traveled);
        if (index < 0) index = ~index;
        var inBend = index > 0 && traveled <= _curves[index - 1].At + _curves[index - 1].ExitDistance;
        for (var i = index; i < _curves.Length && _curves[i].At - traveled <= 500; i++)
            if (Math.Abs(_curves[i].Angle) >= 45)
                return new(traveled, _curves[i].At - traveled, _curves[i].Angle, inBend || _curves[i].At - traveled < 15);
        return new(traveled, InBend: inBend);
    }

    internal (ViewModels.GeoPoint Point, int Bearing) Sample(double distance)
    {
        var index = Array.BinarySearch(_offsets, Math.Clamp(distance, 0, TotalDistance));
        if (index < 0) index = ~index - 1;
        index = Math.Clamp(index, 0, _points.Length - 2);
        while (index < _points.Length - 2 && _offsets[index + 1] <= _offsets[index]) index++;
        var length = _offsets[index + 1] - _offsets[index];
        var fraction = length > 0 ? Math.Clamp((distance - _offsets[index]) / length, 0, 1) : 1;
        var from = _points[index]; var to = _points[index + 1];
        return (new ViewModels.GeoPoint { Lat = from.Lat + (to.Lat - from.Lat) * fraction, Lng = from.Lng + (to.Lng - from.Lng) * fraction }, LocationSimulationService.Bearing(from, to));
    }
}
