using System.Globalization;
using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;

namespace Condor2Nmea.Core.Parsing;

/// <summary>
/// Parses the generic UDP output of Condor 3 (manual chapter 14.1): an ASCII stream of
/// "parameter=value" pairs, one per line, floats with '.' as decimal separator.
/// </summary>
/// <remarks>
/// Mapping rules (learned from XCSoar's Condor3UDP driver):
/// <list type="bullet">
/// <item>altitude is the altimeter (QNH/QFE, m or ft) and is NOT used as navigation altitude.</item>
/// <item>evario follows the STF mode of the electronic vario and is NOT used as TE vario.</item>
/// <item>compass/yaw are not a ground track; vx/vy/vz do not match the track either.</item>
/// <item>bank uses the opposite sign of the aviation convention.</item>
/// </list>
/// </remarks>
public static class UdpPacketParser
{
    /// <summary>
    /// Splits a datagram (or a block of Condor's LogToFile output) into key/value pairs.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Split(string packet)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var rawLine in packet.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t', '\0');
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            if (key.Length > 0)
                result.Add(new(key, value));
        }
        return result;
    }

    /// <summary>Applies a packet to the state. Returns the number of recognised parameters.</summary>
    public static int Apply(string packet, FlightState state, DateTime now)
    {
        var pairs = Split(packet);
        var count = 0;
        state.Update(s =>
        {
            double? lat = null, lon = null;
            foreach (var (key, text) in pairs)
            {
                if (key == "hudmessages")
                {
                    s.HudMessages = text;
                    s.HudMessagesTime = now;
                    count++;
                    continue;
                }
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                    continue;

                s.UdpRaw[key] = v;
                count++;
                const DataSource src = DataSource.Udp;
                switch (key)
                {
                    case "time":
                        s.Set(s.GameTime, v, now, src);
                        if (v >= 0 && v < 24)
                            s.Set(s.TimeOfDay, TimeSpan.FromHours(v), now, src);
                        break;
                    case "altitude": s.Set(s.Altimeter, v, now, src); break;
                    case "vario":
                        s.Set(s.PneumaticVario, v, now, src);
                        s.Set(s.TeVario, v, now, src); // fallback only, NMEA LXWP0 wins while fresh
                        break;
                    case "evario": s.Set(s.ElectronicVario, v, now, src); break;
                    case "nettovario": s.Set(s.NettoVario, v, now, src); break;
                    case "integrator": s.Set(s.Integrator, v, now, src); break;
                    case "compass": s.Set(s.Heading, GeoMath.NormalizeDegrees(v), now, src); break;
                    case "slipball": s.Set(s.SlipBall, GeoMath.ToDegrees(v), now, src); break;
                    case "turnrate": s.Set(s.TurnRate, GeoMath.ToDegrees(v), now, src); break;
                    case "yawstringangle": s.Set(s.YawString, GeoMath.ToDegrees(v), now, src); break;
                    case "yaw": s.Set(s.Yaw, GeoMath.NormalizeDegrees(GeoMath.ToDegrees(v)), now, src); break;
                    case "pitch": s.Set(s.Pitch, GeoMath.ToDegrees(v), now, src); break;
                    case "bank": s.Set(s.Bank, -GeoMath.ToDegrees(v), now, src); break;
                    case "rollrate": s.Set(s.RollRate, GeoMath.ToDegrees(v), now, src); break;
                    case "pitchrate": s.Set(s.PitchRate, GeoMath.ToDegrees(v), now, src); break;
                    case "yawrate": s.Set(s.YawRate, GeoMath.ToDegrees(v), now, src); break;
                    case "gforce": s.Set(s.GLoad, v, now, src); break;
                    case "height": s.Set(s.HeightAboveGround, v, now, src); break;
                    case "wheelheight": s.Set(s.WheelHeight, v, now, src); break;
                    case "turbulencestrength": s.Set(s.Turbulence, v, now, src); break;
                    case "surfaceroughness": s.Set(s.SurfaceRoughness, v, now, src); break;
                    case "flaps": s.Set(s.Flaps, (int)Math.Round(v), now, src); break;
                    case "mc": s.Set(s.MacCready, v, now, src); break;
                    case "water": s.Set(s.WaterBallast, v, now, src); break;
                    // --- not in manual 1.03, seen in real Condor 3 logs ---
                    case "airspeed":
                        s.Set(s.UdpAirspeed, v, now, src);
                        s.Set(s.TrueAirspeed, v, now, src); // fallback only, LXWP0 TAS wins while fresh
                        break;
                    case "radiofrequency": s.Set(s.RadioFrequency, v, now, src); break;
                    // ax/ay/az, quaternionw, vx/vy/vz: kept in UdpRaw only
                    case "latitude": lat = v; break;
                    case "longitude": lon = v; break;
                }
            }
            if (lat is { } la && lon is { } lo)
            {
                var p = new GeoPoint(la, lo);
                if (p.IsValid && !(la == 0 && lo == 0))
                    s.Set(s.Position, p, now, DataSource.Udp);
            }
        });
        return count;
    }
}
