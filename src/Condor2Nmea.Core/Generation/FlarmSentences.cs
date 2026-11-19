using System.Text;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Traffic;

namespace Condor2Nmea.Core.Generation;

/// <summary>
/// FLARM data port sentences for multiplayer traffic, conforming to FTD-012 Data Port ICD 7.23
/// (data port version 9 field layout). Typical order per second: GPRMC, GPGGA, {PFLAA}n, PFLAU.
/// </summary>
public static class FlarmSentences
{
    /// <summary>FTD-012 ch. 7: at most 80 characters between "$" and CR LF.</summary>
    public const int MaxSentenceLength = 80;

    /// <summary>IDType 2 = fixed FLARM ID. Condor ids are not ICAO addresses (1) and not random (0).</summary>
    public const int IdTypeFixedFlarm = 2;

    /// <summary>PFLAU &lt;GPS&gt; field.</summary>
    public enum GpsStatus { NoFix = 0, OnGround = 1, Airborne = 2 }

    public sealed record Options
    {
        /// <summary>
        /// NOT ICD conform: writes "HEXID!CN" into the PFLAA ID field, an XCSoar extension to show the
        /// competition number. Readers that follow the ICD (e.g. Cloudbase) drop such targets. Off by default;
        /// the competition number is sent ICD-conform via PFLAM ACALL anyway.
        /// </summary>
        public bool NonStandardCnInId { get; init; }
        public int MaxTargets { get; init; } = 50;
    }

    public static IEnumerable<string> Pflaa(IReadOnlyList<TrafficTarget> targets, Options options,
        IReadOnlyDictionary<uint, int>? alarmLevels = null)
    {
        foreach (var t in targets.Take(options.MaxTargets))
            yield return PflaaFor(t, options, alarmLevels is not null && alarmLevels.TryGetValue(t.HexId, out var l) ? l : 0);
    }

    /// <summary>
    /// PFLAA,&lt;AlarmLevel&gt;,&lt;RelativeNorth&gt;,&lt;RelativeEast&gt;,&lt;RelativeVertical&gt;,&lt;IDType&gt;,&lt;ID&gt;,&lt;Track&gt;,
    /// &lt;TurnRate&gt;,&lt;GroundSpeed&gt;,&lt;ClimbRate&gt;,&lt;AcftType&gt;,&lt;NoTrack&gt;,&lt;Source&gt;,&lt;RSSI&gt;
    /// </summary>
    public static string PflaaFor(TrafficTarget t, Options options, int alarmLevel = 0)
    {
        var id = t.HexId.ToString("X6");
        if (options.NonStandardCnInId)
        {
            var label = SanitizeLabel(t.Pilot.Cn.Length > 0 ? t.Pilot.Cn : t.Pilot.Registration);
            if (label.Length > 0)
                id += "!" + label;
        }

        // Track: integer 0..359, true ground track
        var track = t.Pilot.Heading is { } h ? (((int)Math.Round(h) % 360 + 360) % 360).ToString(Inv.C) : "";
        // GroundSpeed: integer m/s, forced > 0 while moving, 0 on the ground
        var speed = t.Pilot.Speed is { } sp ? (sp < 0.5 ? 0 : Math.Max(1, (int)Math.Round(sp))).ToString(Inv.C) : "";
        // ClimbRate: one decimal, -32.7 … 32.7
        var climb = t.Pilot.Vario is { } v ? Inv.F(Math.Clamp(v, -32.7, 32.7), 1) : "";
        var north = (long)Math.Round(Math.Clamp(t.RelativeNorth, -20_000_000, 20_000_000));
        var east = (long)Math.Round(Math.Clamp(t.RelativeEast, -20_000_000, 20_000_000));
        var vertical = (int)Math.Round(Math.Clamp(t.RelativeVertical, -32768, 32767));

        // AlarmLevel 0…3, TurnRate empty (ICD: "currently empty"),
        // AcftType 1 = glider, NoTrack 0, Source 0 = FLARM, RSSI empty (unknown)
        return NmeaChecksum.Wrap(string.Create(Inv.C,
            $"PFLAA,{Math.Clamp(alarmLevel, 0, 3)},{north},{east},{vertical},{IdTypeFixedFlarm},{id},{track},,{speed},{climb},1,0,0,"));
    }

    /// <summary>
    /// PFLAU heartbeat, once per second: &lt;RX&gt; = number of received aircraft (0…99), TX and power OK,
    /// GPS status of the own glider, no alarm, no alarm target (bearing, vertical, distance and ID empty).
    /// </summary>
    public static string Pflau(int count, GpsStatus gps = GpsStatus.Airborne) =>
        NmeaChecksum.Wrap(string.Create(Inv.C, $"PFLAU,{Math.Clamp(count, 0, 99)},1,{(int)gps},1,0,,0,,,"));

    /// <summary>
    /// PFLAU with the most dangerous aircraft: &lt;AlarmLevel&gt;, &lt;RelativeBearing&gt; (−180…180 from the own
    /// ground track, clockwise), &lt;AlarmType&gt; 2 = aircraft alarm, &lt;RelativeVertical&gt;, &lt;RelativeDistance&gt;, &lt;ID&gt;.
    /// </summary>
    public static string PflauAlarm(int count, GpsStatus gps, int alarmLevel, TrafficTarget target, double ownTrack)
    {
        var rel = target.Bearing - ownTrack;
        while (rel > 180) rel -= 360;
        while (rel < -180) rel += 360;
        var bearing = (int)Math.Round(rel);
        var vertical = (int)Math.Round(Math.Clamp(target.RelativeVertical, -32768, 32767));
        var distance = (long)Math.Round(target.Distance);
        return NmeaChecksum.Wrap(string.Create(Inv.C,
            $"PFLAU,{Math.Clamp(count, 0, 99)},1,{(int)gps},1,{Math.Clamp(alarmLevel, 1, 3)},{bearing},2,{vertical},{distance},{target.HexId:X6}"));
    }

    /// <summary>
    /// PFLAM messaging (FTD-109): pilot name, competition id (call sign), registration and aircraft type of
    /// another aircraft. Payload is hex encoded UTF-8; the sentence stays within 80 characters.
    /// </summary>
    public static IEnumerable<string> Pflam(TrafficTarget t)
    {
        var p = t.Pilot;
        if (p.PilotName.Length > 0) yield return PflamLine(t.HexId, "PNAME", p.PilotName);
        if (p.Cn.Length > 0) yield return PflamLine(t.HexId, "ACALL", p.Cn);
        if (p.Registration.Length > 0) yield return PflamLine(t.HexId, "AREG", p.Registration);
        if (p.Plane.Length > 0) yield return PflamLine(t.HexId, "ATYPE", p.Plane);
    }

    private static string PflamLine(uint id, string type, string text)
    {
        var prefix = $"PFLAM,U,{IdTypeFixedFlarm},{id:X6},{type},";
        // 80 characters between "$" and CR LF, minus "*hh"
        var maxBytes = (MaxSentenceLength - prefix.Length - 3) / 2;
        return NmeaChecksum.Wrap(prefix + HexUtf8(text, maxBytes));
    }

    /// <summary>Hex encoded UTF-8 text, cut at a character boundary so that it fits <paramref name="maxBytes"/>.</summary>
    internal static string HexUtf8(string text, int maxBytes)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
            if (!char.IsControl(ch))
                clean.Append(ch);
        var s = clean.ToString();
        var bytes = Encoding.UTF8.GetBytes(s);
        while (bytes.Length > maxBytes && s.Length > 0)
        {
            s = s[..^1];
            if (s.Length > 0 && char.IsHighSurrogate(s[^1]))
                s = s[..^1];
            bytes = Encoding.UTF8.GetBytes(s);
        }
        return Convert.ToHexString(bytes);
    }

    private static string SanitizeLabel(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
            if (ch is > ' ' and <= '~' and not (',' or '*' or '$' or '!'))
                sb.Append(ch);
        return sb.Length > 8 ? sb.ToString(0, 8) : sb.ToString();
    }
}
