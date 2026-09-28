using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public class GeoCoordinateConverterTests
{
    [Fact]
    public void Wgs84AndGcj02_RoundTripWithinOneMeter()
    {
        var wgs = new GeoCoordinate(39.9042, 116.4074);
        var gcj = GeoCoordinateConverter.Convert(wgs.Latitude, wgs.Longitude,
            GeoCoordinateSystem.Wgs84, GeoCoordinateSystem.Gcj02);

        Assert.True(Math.Abs(gcj.Latitude - wgs.Latitude) > 0.0001);
        Assert.True(Math.Abs(gcj.Longitude - wgs.Longitude) > 0.0001);

        var roundTrip = GeoCoordinateConverter.Convert(gcj.Latitude, gcj.Longitude,
            GeoCoordinateSystem.Gcj02, GeoCoordinateSystem.Wgs84);
        Assert.InRange(Math.Abs(roundTrip.Latitude - wgs.Latitude), 0, 0.00002);
        Assert.InRange(Math.Abs(roundTrip.Longitude - wgs.Longitude), 0, 0.00002);
    }

    [Fact]
    public void Bd09AndWgs84_RoundTripWithinOneMeter()
    {
        var wgs = new GeoCoordinate(39.9042, 116.4074);
        var bd09 = GeoCoordinateConverter.Convert(wgs.Latitude, wgs.Longitude,
            GeoCoordinateSystem.Wgs84, GeoCoordinateSystem.Bd09);
        var roundTrip = GeoCoordinateConverter.Convert(bd09.Latitude, bd09.Longitude,
            GeoCoordinateSystem.Bd09, GeoCoordinateSystem.Wgs84);

        Assert.InRange(Math.Abs(roundTrip.Latitude - wgs.Latitude), 0, 0.00002);
        Assert.InRange(Math.Abs(roundTrip.Longitude - wgs.Longitude), 0, 0.00002);
    }

    [Fact]
    public void CoordinatesOutsideChina_AreNotShifted()
    {
        var coordinate = GeoCoordinateConverter.Convert(40.7128, -74.0060,
            GeoCoordinateSystem.Wgs84, GeoCoordinateSystem.Gcj02);

        Assert.Equal(40.7128, coordinate.Latitude, 10);
        Assert.Equal(-74.0060, coordinate.Longitude, 10);
    }

    [Theory]
    [InlineData("gps", GeoCoordinateSystem.Wgs84)]
    [InlineData("高德", GeoCoordinateSystem.Gcj02)]
    [InlineData("bd-09", GeoCoordinateSystem.Bd09)]
    public void TryParseRecognizesAliases(string value, GeoCoordinateSystem expected)
    {
        Assert.True(GeoCoordinateConverter.TryParse(value, out var actual));
        Assert.Equal(expected, actual);
    }
}
