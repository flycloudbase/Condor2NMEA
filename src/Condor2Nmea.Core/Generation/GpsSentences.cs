using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;

namespace Condor2Nmea.Core.Generation;

public static class GpsSentences
{
    private const double MsToKnots = 1.0 / 0.514444;

    /// <summary>$GPRMC and $GPGGA, or nothing if no fresh position is available.</summary>
    public static IEnumerable<string> Build(FlightSnapshot s, DateTime utcNow)
    {
        if (s.Position is null)
            yield break;
        yield return Rmc(s, utcNow)!;
        yield return Gga(s, utcNow)!;
    }

    public static string? Rmc(FlightSnapshot s, DateTime utcNow)
    {
        if (s.Position is not { } p)
            return null;
        var time = NmeaFormat.Time(s.TimeOfDay ?? utcNow.TimeOfDay);
        var date = NmeaFormat.Date(s.Date ?? DateOnly.FromDateTime(utcNow));
        var speed = s.GroundSpeed is { } gs ? Inv.F(gs * MsToKnots, 1) : "";
        var track = s.Track is { } tr ? Inv.F(tr, 1) : "";
        return NmeaChecksum.Wrap(
            $"GPRMC,{time},A,{NmeaFormat.Latitude(p.Latitude)},{NmeaFormat.Longitude(p.Longitude)},{speed},{track},{date},,,A");
    }

    /// <summary>
    /// Condor's own $GPGGA has an empty geoid separation, which makes some receivers (XCSoar) subtract
    /// EGM96 from an altitude that already is MSL. We always write a separation of 0.0.
    /// </summary>
    public static string? Gga(FlightSnapshot s, DateTime utcNow)
    {
        if (s.Position is not { } p)
            return null;
        var time = NmeaFormat.Time(s.TimeOfDay ?? utcNow.TimeOfDay);
        var altValue = s.GpsAltitude ?? s.BaroAltitude;
        var alt = altValue is { } a ? Inv.F(a, 1) : "";
        return NmeaChecksum.Wrap(
            $"GPGGA,{time},{NmeaFormat.Latitude(p.Latitude)},{NmeaFormat.Longitude(p.Longitude)},1,08,1.0,{alt},M,0.0,M,,");
    }
}
