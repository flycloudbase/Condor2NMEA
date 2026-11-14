using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;

namespace Condor2Nmea.Core.Parsing;

/// <summary>
/// Parses the serial NMEA output of Condor ($GPRMC, $GPGGA, $LXWP0) into a <see cref="FlightState"/>.
/// </summary>
public sealed class CondorNmeaParser
{
    private const double KnotsToMs = 0.514444;
    private const double KmhToMs = 1.0 / 3.6;

    /// <summary>
    /// Condor 1/2 send the direction the wind blows TO in LXWP0, Condor 3 the direction it comes FROM.
    /// </summary>
    public bool WindDirectionIsTo { get; set; }

    /// <summary>Returns true if the sentence was recognised.</summary>
    public bool Apply(NmeaLine line, FlightState state, DateTime now)
    {
        var type = line.Type;
        // accept any talker id for the standard sentences (GP, GN, ...)
        if (type.Length == 5 && type.EndsWith("RMC", StringComparison.Ordinal))
            return ParseRmc(line, state, now);
        if (type.Length == 5 && type.EndsWith("GGA", StringComparison.Ordinal))
            return ParseGga(line, state, now);
        if (type == "LXWP0")
            return ParseLxwp0(line, state, now);
        return false;
    }

    private static bool ParseRmc(NmeaLine l, FlightState state, DateTime now)
    {
        // $GPRMC,time,status,lat,N,lon,E,speed[kn],track,date,magvar,E[,mode]
        if (l[0].Length == 0 && l[2].Length == 0)
            return false;
        var time = NmeaFormat.ParseTime(l[0]);
        var valid = l[1] == "A";
        var lat = NmeaFormat.ParseCoordinate(l[2], l[3]);
        var lon = NmeaFormat.ParseCoordinate(l[4], l[5]);
        var speed = l.Double(6);
        var track = l.Double(7);
        var date = NmeaFormat.ParseDate(l[8]);

        state.Update(s =>
        {
            if (time is { } t) s.Set(s.TimeOfDay, t, now, DataSource.Nmea);
            if (date is { } d) s.Set(s.Date, d, now, DataSource.Nmea);
            if (!valid) return;
            if (lat is { } la && lon is { } lo)
            {
                var p = new GeoPoint(la, lo);
                if (p.IsValid) s.Set(s.Position, p, now, DataSource.Nmea);
            }
            if (speed is { } sp) s.Set(s.GroundSpeed, sp * KnotsToMs, now, DataSource.Nmea);
            if (track is { } tr) s.Set(s.Track, GeoMath.NormalizeDegrees(tr), now, DataSource.Nmea);
        });
        return true;
    }

    private static bool ParseGga(NmeaLine l, FlightState state, DateTime now)
    {
        // $GPGGA,time,lat,N,lon,E,fix,sats,hdop,alt,M,geoid,M,age,station
        var time = NmeaFormat.ParseTime(l[0]);
        var lat = NmeaFormat.ParseCoordinate(l[1], l[2]);
        var lon = NmeaFormat.ParseCoordinate(l[3], l[4]);
        var fix = l.Double(5) ?? 0;
        var alt = l.Double(8);
        // Condor leaves the geoid separation empty, the altitude is MSL. If a separation is
        // present the altitude is still MSL per NMEA definition.

        state.Update(s =>
        {
            if (time is { } t) s.Set(s.TimeOfDay, t, now, DataSource.Nmea);
            if (fix <= 0) return;
            if (lat is { } la && lon is { } lo)
            {
                var p = new GeoPoint(la, lo);
                if (p.IsValid) s.Set(s.Position, p, now, DataSource.Nmea);
            }
            if (alt is { } a) s.Set(s.GpsAltitude, a, now, DataSource.Nmea);
        });
        return true;
    }

    private bool ParseLxwp0(NmeaLine l, FlightState state, DateTime now)
    {
        // $LXWP0,Y,tas[km/h],baroalt[m],vario1..vario6[m/s],heading,winddir,windspeed[km/h]
        // Field 1 is IAS in the LX spec, but Condor puts TAS there.
        var tas = l.Double(1);
        var alt = l.Double(2);
        var vario = l.Double(3);
        var heading = l.Double(9);
        var windDir = l.Double(10);
        var windSpeed = l.Double(11);

        state.Update(s =>
        {
            if (tas is { } v) s.Set(s.TrueAirspeed, v * KmhToMs, now, DataSource.Nmea);
            if (alt is { } a) s.Set(s.BaroAltitude, a, now, DataSource.Nmea);
            if (vario is { } va) s.Set(s.TeVario, va, now, DataSource.Nmea);
            if (heading is { } h) s.Set(s.Heading, GeoMath.NormalizeDegrees(h), now, DataSource.Nmea);
            if (windDir is { } wd && windSpeed is { } ws)
            {
                var from = WindDirectionIsTo ? wd + 180.0 : wd;
                s.Set(s.WindDirection, GeoMath.NormalizeDegrees(from), now, DataSource.Nmea);
                s.Set(s.WindSpeed, ws * KmhToMs, now, DataSource.Nmea);
            }
        });
        return true;
    }
}
