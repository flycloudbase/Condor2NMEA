using System.Text;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Traffic;

namespace Condor2Nmea.Core.Generation;

/// <summary>FLARM data port sentences (FTD-012) for multiplayer traffic.</summary>
public static class FlarmSentences
{
    public sealed record Options
    {
        /// <summary>Write "HEXID!CN" in PFLAA so programs like XCSoar can show the competition number.</summary>
        public bool AppendCnToId { get; init; } = true;
        public int MaxTargets { get; init; } = 50;
    }

    public static IEnumerable<string> Pflaa(IReadOnlyList<TrafficTarget> targets, Options options)
    {
        foreach (var t in targets.Take(options.MaxTargets))
            yield return PflaaFor(t, options);
    }

    public static string PflaaFor(TrafficTarget t, Options options)
    {
        var id = t.HexId.ToString("X6");
        var label = SanitizeLabel(t.Pilot.Cn.Length > 0 ? t.Pilot.Cn : t.Pilot.Registration);
        if (options.AppendCnToId && label.Length > 0)
            id += "!" + label;
        var track = t.Pilot.Heading is { } h ? ((int)Math.Round(h) % 360).ToString(Inv.C) : "";
        var speed = t.Pilot.Speed is { } sp ? Inv.F(Math.Max(0, sp), 0) : "";
        var climb = t.Pilot.Vario is { } v ? Inv.F(v, 1) : "";
        // $PFLAA,AlarmLevel,RelN,RelE,RelV,IDType,ID,Track,TurnRate,GroundSpeed,ClimbRate,AcftType
        return NmeaChecksum.Wrap(string.Create(Inv.C,
            $"PFLAA,0,{Math.Round(t.RelativeNorth):0},{Math.Round(t.RelativeEast):0},{Math.Round(t.RelativeVertical):0},1,{id},{track},,{speed},{climb},1"));
    }

    /// <summary>$PFLAU status: RX count, TX ok, 3D fix, power ok, no alarm.</summary>
    public static string Pflau(int count) =>
        NmeaChecksum.Wrap(string.Create(Inv.C, $"PFLAU,{Math.Min(count, 99)},1,2,1,0,,0,,,"));

    /// <summary>
    /// $PFLAM name/identification messages (pilot name, competition id, registration, type) as used
    /// by XCSoar. Payloads are hex encoded ASCII.
    /// </summary>
    public static IEnumerable<string> Pflam(TrafficTarget t)
    {
        var p = t.Pilot;
        if (p.PilotName.Length > 0) yield return PflamLine(t.HexId, "PNAME", p.PilotName);
        if (p.Cn.Length > 0) yield return PflamLine(t.HexId, "ACALL", p.Cn);
        if (p.Registration.Length > 0) yield return PflamLine(t.HexId, "AREG", p.Registration);
        if (p.Plane.Length > 0) yield return PflamLine(t.HexId, "ATYPE", p.Plane);
    }

    private static string PflamLine(uint id, string type, string text) =>
        NmeaChecksum.Wrap($"PFLAM,U,2,{id:X6},{type},{Hex(text, 17)}");

    private static string Hex(string text, int maxChars)
    {
        var bytes = Encoding.ASCII.GetBytes(Ascii(text));
        var sb = new StringBuilder();
        foreach (var b in bytes.Take(maxChars))
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    private static string Ascii(string s)
    {
        string decomposed;
        try { decomposed = s.Normalize(NormalizationForm.FormD); }
        catch (Exception) { decomposed = s; }
        var sb = new StringBuilder(s.Length);
        foreach (var ch in decomposed)
        {
            if (ch < 32 || ch > 126)
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string SanitizeLabel(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in Ascii(s))
            if (ch is not (',' or '*' or '$' or '!' or ' '))
                sb.Append(ch);
        return sb.Length > 8 ? sb.ToString(0, 8) : sb.ToString();
    }
}
