using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;

namespace Condor2Nmea.Core.Generation;

public static class LxSentences
{
    /// <summary>
    /// $LXWP0 as Condor sends it (field 1 = TAS in km/h). Readers such as XCSoar's
    /// "Condor Soaring Simulator 3" driver expect it exactly like this.
    /// </summary>
    /// <param name="windDirectionTo">true: write the direction the wind blows to (Condor 1/2 style).</param>
    public static string? Lxwp0(FlightSnapshot s, bool windDirectionTo = false)
    {
        if (s.TrueAirspeed is null && s.BaroAltitude is null && s.TeVario is null)
            return null;
        var tas = s.TrueAirspeed is { } v ? Inv.F(v * 3.6, 1) : "";
        var alt = s.BaroAltitude is { } a ? Inv.F(a, 1) : "";
        var vario = s.TeVario is { } va ? Inv.F(va, 2) : "";
        var heading = s.Heading is { } h ? Inv.F(h, 0) : "";
        string windDir = "", windSpeed = "";
        if (s.WindDirection is { } wd && s.WindSpeed is { } ws)
        {
            windDir = Inv.F((windDirectionTo ? wd + 180 : wd) % 360, 0);
            windSpeed = Inv.F(ws * 3.6, 1);
        }
        return NmeaChecksum.Wrap($"LXWP0,Y,{tas},{alt},{vario},,,,,,{heading},{windDir},{windSpeed}");
    }

    /// <summary>$LXWP2,MC,ballast,bugs,... – only MacCready is known reliably.</summary>
    public static string? Lxwp2(FlightSnapshot s)
    {
        if (s.MacCready is not { } mc)
            return null;
        return NmeaChecksum.Wrap($"LXWP2,{Inv.F(mc, 1)},,,,,,");
    }
}
