namespace TerminalSimulation.Wpf.Services;

internal sealed class LocationSimulationService : ILocationSimulationService
{
    public async Task RunAsync(IReadOnlyList<ViewModels.GeoPoint> path, Func<double> speedKph,
        Action<ViewModels.GeoPoint, int> positionChanged, CancellationToken cancellationToken)
    {
        if (path.Count < 2) throw new ArgumentException("路径至少需要两个点", nameof(path));
        var segmentDistances = Enumerable.Range(0, path.Count - 1)
            .Select(index => Distance(path[index], path[index + 1])).ToArray();
        var totalDistance = segmentDistances.Sum();
        var traveled = 0d;

        while (traveled < totalDistance)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var accumulated = 0d;
            var segment = 0;
            for (; segment < segmentDistances.Length - 1; segment++)
            {
                if (traveled <= accumulated + segmentDistances[segment]) break;
                accumulated += segmentDistances[segment];
            }
            var length = segmentDistances[segment];
            var fraction = length > 0 ? Math.Clamp((traveled - accumulated) / length, 0, 1) : 0;
            var from = path[segment];
            var to = path[segment + 1];
            var point = new ViewModels.GeoPoint
            {
                Lat = from.Lat + (to.Lat - from.Lat) * fraction,
                Lng = from.Lng + (to.Lng - from.Lng) * fraction
            };
            positionChanged(point, Bearing(from, to));
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            traveled += Math.Max(0, speedKph()) * 1000d / 3600d * 0.1d;
        }
    }

    private static double Distance(ViewModels.GeoPoint from, ViewModels.GeoPoint to)
    {
        const double earthRadius = 6371e3;
        var phi1 = from.Lat * Math.PI / 180;
        var phi2 = to.Lat * Math.PI / 180;
        var deltaPhi = (to.Lat - from.Lat) * Math.PI / 180;
        var deltaLambda = (to.Lng - from.Lng) * Math.PI / 180;
        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static int Bearing(ViewModels.GeoPoint from, ViewModels.GeoPoint to)
    {
        var phi1 = from.Lat * Math.PI / 180;
        var phi2 = to.Lat * Math.PI / 180;
        var deltaLambda = (to.Lng - from.Lng) * Math.PI / 180;
        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
        return (int)Math.Round((Math.Atan2(y, x) * 180 / Math.PI + 360) % 360);
    }
}
