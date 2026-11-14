using System.Text;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Core.Generation;

/// <summary>
/// Proprietary sentence family <c>$PCBC</c> for Cloudbase (flycloudbase/cloudbase). It carries everything Condor knows
/// that GPS, LXWP0 and FLARM sentences cannot transport. Specification: docs/PCBC.md.
/// <list type="bullet">
/// <item><c>$PCBC,DATA,…</c> – flight data, at the vario rate</item>
/// <item><c>$PCBC,INFO,…</c> – own glider and race, every few seconds</item>
/// <item><c>$PCBC,EVENT,…</c> – Condor HUD messages (release, landing, …) once when they appear</item>
/// </list>
/// All fields are positional; an empty field means "unknown". New fields are only ever appended, older readers
/// ignore them. Units are SI (m, m/s, kg) and degrees.
/// </summary>
public static class CloudbaseSentences
{
    public const int Version = 1;

    /// <summary>Wheel height below which the glider counts as standing / rolling on the ground [m].</summary>
    public const double OnGroundWheelHeight = 0.3;

    public static string? Data(FlightSnapshot s)
    {
        string F(double? v, int decimals) => v is { } x ? Inv.F(x, decimals) : "";
        var anyValue = s.TrueAirspeed is not null || s.TeVario is not null || s.NettoVario is not null || s.Bank is not null;
        if (!anyValue)
            return null;

        var onGround = s.WheelHeight is { } wh ? (wh < OnGroundWheelHeight ? "1" : "0") : "";
        var fields = new[]
        {
            "DATA",
            Version.ToString(Inv.C),
            s.TimeOfDay is { } t ? NmeaFormat.Time(t) : "",   // 2 sim time
            F(s.Airspeed, 1),                                  // 3 IAS
            F(s.TrueAirspeed, 1),                              // 4 TAS
            F(s.TeVario, 2),                                   // 5 TE vario
            F(s.NettoVario, 2),                                // 6 netto
            F(s.PneumaticVario, 2),                            // 7 uncompensated
            F(s.ElectronicVario, 2),                           // 8 electronic vario (follows STF mode)
            F(s.Integrator, 2),                                // 9 integrator / average
            F(s.Heading, 0),                                   // 10 heading
            F(s.Bank, 1),                                      // 11 bank, + right wing down
            F(s.Pitch, 1),                                     // 12 pitch, + nose up
            F(s.GLoad, 2),                                     // 13 g load
            F(s.HeightAboveGround, 1),                         // 14 height above ground
            onGround,                                          // 15 on ground
            F(s.MacCready, 1),                                 // 16 MacCready
            F(s.WaterBallast, 0),                              // 17 water ballast kg
            s.Flaps?.ToString(Inv.C) ?? "",                    // 18 flaps index
            F(s.Turbulence, 2),                                // 19 turbulence strength
            F(s.RadioFrequency, 3),                            // 20 COM frequency MHz
        };
        return NmeaChecksum.Wrap("PCBC," + string.Join(',', fields));
    }

    /// <param name="own">Own glider from Spectate (null if unknown).</param>
    /// <param name="pilotCount">Number of pilots in the session (Spectate), null if no Spectate data.</param>
    /// <param name="software">e.g. "Condor2NMEA 0.2.0"</param>
    public static string Info(SpectatePilot? own, int? pilotCount, string software)
    {
        var fields = new[]
        {
            "INFO",
            Version.ToString(Inv.C),
            "Condor",                                  // 2 simulator
            Text(software, 24),                        // 3 sending software
            Text(own?.Cn, 8),                          // 4 competition id
            Text(own?.Registration, 12),               // 5 registration
            Text(own?.Plane, 20),                      // 6 aircraft type
            Text(own?.PilotName, 30),                  // 7 pilot
            Text(own?.Country, 20),                    // 8 country
            Text(own?.PlayerStatus, 16),               // 9 race status
            Text(own?.Rank, 4),                        // 10 rank
            Number(own?.Score),                        // 11 score points
            Number(own?.Distance),                     // 12 task distance km
            Number(own?.AverageSpeed),                 // 13 average speed km/h
            Text(own?.TaskTime, 10),                   // 14 race time
            pilotCount?.ToString(Inv.C) ?? "",         // 15 pilots in session
        };
        return NmeaChecksum.Wrap("PCBC," + string.Join(',', fields));
    }

    public static string Event(TimeSpan? simTime, string message) =>
        NmeaChecksum.Wrap($"PCBC,EVENT,{Version},{(simTime is { } t ? NmeaFormat.Time(t) : "")},{Text(message, 120)}");

    /// <summary>
    /// Makes text NMEA-safe: ASCII only (accents removed), no field or sentence delimiters.
    /// </summary>
    public static string Text(string? s, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim() is "—" or "-" or "–")
            return "";
        string decomposed;
        try { decomposed = s.Normalize(NormalizationForm.FormD); }
        catch (Exception) { decomposed = s; }
        var sb = new StringBuilder(s.Length);
        foreach (var ch in decomposed.Replace("ß", "ss"))
        {
            if (ch is < ' ' or > '~' or ',' or '*' or '$' or '!' or '\\' or '^')
                continue;
            sb.Append(ch);
        }
        var r = sb.ToString().Trim();
        return r.Length > maxLength ? r[..maxLength].TrimEnd() : r;
    }

    /// <summary>"999.7 p" → "999.7", "210.7 km/h" → "210.7", "—" → "".</summary>
    private static string Number(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        var t = s.Trim();
        var end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] is '.' or '-' or '+'))
            end++;
        return end > 0 && Inv.TryDouble(t[..end], out var v) ? Inv.F(v, 1) : "";
    }
}
