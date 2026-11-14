using System.Text;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;

namespace Condor2Nmea.Core.Generation;

/// <summary>
/// OpenVario "$POV" sentence: lets XCSoar (driver "OpenVario") receive TE vario, uncompensated
/// vario, TAS and g-load in addition to the Condor driver.
/// </summary>
public static class OpenVarioSentences
{
    public static string? Pov(FlightSnapshot s)
    {
        var sb = new StringBuilder("POV");
        var any = false;
        void Add(char code, double? value, int decimals)
        {
            if (value is not { } v) return;
            sb.Append(',').Append(code).Append(',').Append(Inv.F(v, decimals));
            any = true;
        }
        Add('E', s.TeVario, 2);
        Add('e', s.PneumaticVario, 2);
        Add('S', s.TrueAirspeed * 3.6, 1);
        Add('L', s.GLoad, 2);
        return any ? NmeaChecksum.Wrap(sb.ToString()) : null;
    }
}
