namespace Condor2Nmea.Core.Geo;

public static class GeoMath
{
    public const double EarthRadius = 6378137.0; // WGS84 equator radius [m]

    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    public static double ToDegrees(double radians) => radians * 180.0 / Math.PI;

    /// <summary>
    /// Local flat-earth offset of <paramref name="target"/> relative to <paramref name="reference"/>
    /// in metres (north, east). Accurate enough for FLARM ranges (some tens of km).
    /// </summary>
    public static (double North, double East) RelativeNorthEast(GeoPoint reference, GeoPoint target)
    {
        var dLat = ToRadians(target.Latitude - reference.Latitude);
        var dLon = ToRadians(NormalizeLongitudeDelta(target.Longitude - reference.Longitude));
        var north = dLat * EarthRadius;
        var east = dLon * EarthRadius * Math.Cos(ToRadians(reference.Latitude));
        return (north, east);
    }

    /// <summary>Moves a point by a distance [m] along a true course [deg].</summary>
    public static GeoPoint Offset(GeoPoint start, double courseDegrees, double distanceMeters)
    {
        var c = ToRadians(courseDegrees);
        var north = Math.Cos(c) * distanceMeters;
        var east = Math.Sin(c) * distanceMeters;
        var lat = start.Latitude + ToDegrees(north / EarthRadius);
        var cosLat = Math.Cos(ToRadians(start.Latitude));
        var lon = start.Longitude + (cosLat > 1e-9 ? ToDegrees(east / (EarthRadius * cosLat)) : 0);
        return new GeoPoint(lat, NormalizeLongitude(lon));
    }

    public static double Distance(GeoPoint a, GeoPoint b)
    {
        var (n, e) = RelativeNorthEast(a, b);
        return Math.Sqrt(n * n + e * e);
    }

    public static double NormalizeDegrees(double degrees)
    {
        var d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private static double NormalizeLongitudeDelta(double d)
    {
        while (d > 180) d -= 360;
        while (d < -180) d += 360;
        return d;
    }

    private static double NormalizeLongitude(double lon) => NormalizeLongitudeDelta(lon);
}
