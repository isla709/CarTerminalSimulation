namespace TerminalSimulation.Wpf.Services;

/// <summary>
/// Coordinate systems used by the map providers and by JT/T 808.
/// JT/T 808 location reports use WGS-84; AMap uses GCJ-02 and Baidu maps use BD-09.
/// </summary>
public enum GeoCoordinateSystem
{
    Wgs84,
    Gcj02,
    Bd09
}

public readonly record struct GeoCoordinate(double Latitude, double Longitude);

public static class GeoCoordinateConverter
{
    private const double EarthSemiMajorAxis = 6378245.0;
    private const double EccentricitySquared = 0.00669342162296594323;
    private const double Pi = Math.PI;
    private const double XPi = Pi * 3000.0 / 180.0;

    public static GeoCoordinate Convert(
        double latitude,
        double longitude,
        GeoCoordinateSystem from,
        GeoCoordinateSystem to)
    {
        if (from == to)
        {
            return new GeoCoordinate(latitude, longitude);
        }

        var wgs = from switch
        {
            GeoCoordinateSystem.Wgs84 => new GeoCoordinate(latitude, longitude),
            GeoCoordinateSystem.Gcj02 => Gcj02ToWgs84(latitude, longitude),
            GeoCoordinateSystem.Bd09 => Gcj02ToWgs84(Bd09ToGcj02(latitude, longitude).Latitude,
                Bd09ToGcj02(latitude, longitude).Longitude),
            _ => throw new ArgumentOutOfRangeException(nameof(from))
        };

        return to switch
        {
            GeoCoordinateSystem.Wgs84 => wgs,
            GeoCoordinateSystem.Gcj02 => Wgs84ToGcj02(wgs.Latitude, wgs.Longitude),
            GeoCoordinateSystem.Bd09 => Gcj02ToBd09(Wgs84ToGcj02(wgs.Latitude, wgs.Longitude).Latitude,
                Wgs84ToGcj02(wgs.Latitude, wgs.Longitude).Longitude),
            _ => throw new ArgumentOutOfRangeException(nameof(to))
        };
    }

    public static bool TryParse(string? value, out GeoCoordinateSystem coordinateSystem)
    {
        coordinateSystem = value?.Trim().ToLowerInvariant() switch
        {
            "wgs84" or "wgs-84" or "gps" => GeoCoordinateSystem.Wgs84,
            "bd09" or "bd-09" or "baidu" or "百度" => GeoCoordinateSystem.Bd09,
            "gcj02" or "gcj-02" or "amap" or "gaode" or "高德" => GeoCoordinateSystem.Gcj02,
            _ => GeoCoordinateSystem.Gcj02
        };
        return value is not null && value.Trim().Length > 0;
    }

    public static string ToProtocolName(this GeoCoordinateSystem coordinateSystem) => coordinateSystem switch
    {
        GeoCoordinateSystem.Wgs84 => "WGS-84",
        GeoCoordinateSystem.Gcj02 => "GCJ-02",
        GeoCoordinateSystem.Bd09 => "BD-09",
        _ => coordinateSystem.ToString()
    };

    private static GeoCoordinate Wgs84ToGcj02(double latitude, double longitude)
    {
        if (OutOfChina(latitude, longitude)) return new GeoCoordinate(latitude, longitude);
        var dLat = TransformLatitude(longitude - 105.0, latitude - 35.0);
        var dLon = TransformLongitude(longitude - 105.0, latitude - 35.0);
        var radLat = latitude / 180.0 * Pi;
        var magic = 1 - EccentricitySquared * Math.Sin(radLat) * Math.Sin(radLat);
        var sqrtMagic = Math.Sqrt(magic);
        dLat = dLat * 180.0 / ((EarthSemiMajorAxis * (1 - EccentricitySquared)) / (magic * sqrtMagic) * Pi);
        dLon = dLon * 180.0 / (EarthSemiMajorAxis / sqrtMagic * Math.Cos(radLat) * Pi);
        return new GeoCoordinate(latitude + dLat, longitude + dLon);
    }

    private static GeoCoordinate Gcj02ToWgs84(double latitude, double longitude)
    {
        if (OutOfChina(latitude, longitude)) return new GeoCoordinate(latitude, longitude);
        // Invert the GCJ-02 transform iteratively.  The common one-step
        // approximation can leave a visible error after a BD-09 round trip.
        var estimate = new GeoCoordinate(latitude, longitude);
        for (var i = 0; i < 8; i++)
        {
            var transformed = Wgs84ToGcj02(estimate.Latitude, estimate.Longitude);
            var dLat = latitude - transformed.Latitude;
            var dLon = longitude - transformed.Longitude;
            estimate = new GeoCoordinate(estimate.Latitude + dLat, estimate.Longitude + dLon);
            if (Math.Abs(dLat) < 1e-10 && Math.Abs(dLon) < 1e-10) break;
        }
        return estimate;
    }

    private static GeoCoordinate Gcj02ToBd09(double latitude, double longitude)
    {
        var x = longitude;
        var y = latitude;
        var z = Math.Sqrt(x * x + y * y) + 0.00002 * Math.Sin(y * XPi);
        var theta = Math.Atan2(y, x) + 0.000003 * Math.Cos(x * XPi);
        return new GeoCoordinate(z * Math.Sin(theta) + 0.006, z * Math.Cos(theta) + 0.0065);
    }

    private static GeoCoordinate Bd09ToGcj02(double latitude, double longitude)
    {
        var x = longitude - 0.0065;
        var y = latitude - 0.006;
        var z = Math.Sqrt(x * x + y * y) - 0.00002 * Math.Sin(y * XPi);
        var theta = Math.Atan2(y, x) - 0.000003 * Math.Cos(x * XPi);
        return new GeoCoordinate(z * Math.Sin(theta), z * Math.Cos(theta));
    }

    private static bool OutOfChina(double latitude, double longitude) =>
        longitude < 72.004 || longitude > 137.8347 || latitude < 0.8293 || latitude > 55.8271;

    private static double TransformLatitude(double x, double y)
    {
        var ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
        ret += (20.0 * Math.Sin(6.0 * x * Pi) + 20.0 * Math.Sin(2.0 * x * Pi)) * 2.0 / 3.0;
        ret += (20.0 * Math.Sin(y * Pi) + 40.0 * Math.Sin(y / 3.0 * Pi)) * 2.0 / 3.0;
        ret += (160.0 * Math.Sin(y / 12.0 * Pi) + 320 * Math.Sin(y * Pi / 30.0)) * 2.0 / 3.0;
        return ret;
    }

    private static double TransformLongitude(double x, double y)
    {
        var ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
        ret += (20.0 * Math.Sin(6.0 * x * Pi) + 20.0 * Math.Sin(2.0 * x * Pi)) * 2.0 / 3.0;
        ret += (20.0 * Math.Sin(x * Pi) + 40.0 * Math.Sin(x / 3.0 * Pi)) * 2.0 / 3.0;
        ret += (150.0 * Math.Sin(x / 12.0 * Pi) + 300.0 * Math.Sin(x / 30.0 * Pi)) * 2.0 / 3.0;
        return ret;
    }
}
