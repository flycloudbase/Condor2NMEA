namespace Condor2Nmea.Core.Geo;

/// <summary>WGS84 position in decimal degrees (north and east positive).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid =>
        double.IsFinite(Latitude) && double.IsFinite(Longitude) &&
        Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180;

    public override string ToString() =>
        FormattableString.Invariant($"{Latitude:F6}, {Longitude:F6}");
}
