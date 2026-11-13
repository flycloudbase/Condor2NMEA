using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Condor2Nmea.Core.Nmea;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>
/// Builds a replay recording from Condor's own logs: a captured NMEA stream (one sentence per line),
/// the UDP LogToFile output (packets separated by empty lines) and a Spectate.json snapshot.
/// The logs need not come from the same flight; each is timed by its own clock.
/// </summary>
public static class LogConverter
{
    public static int Convert(string? nmeaLog, string? udpLog, string? spectateJson, string output, TimeSpan? maxDuration = null)
    {
        var events = new List<(long Ms, string Channel, string Payload)>();
        long end = 0;

        if (nmeaLog is not null)
        {
            TimeSpan? first = null;
            foreach (var raw in File.ReadLines(nmeaLog, Encoding.Latin1))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var parsed = NmeaLine.TryParse(line);
                if (parsed is not null && (parsed.Type.EndsWith("GGA") || parsed.Type.EndsWith("RMC")) &&
                    NmeaFormat.ParseTime(parsed[0]) is { } t)
                {
                    first ??= t;
                    var ms = (long)(t - first.Value).TotalMilliseconds;
                    if (ms < 0) ms += 86_400_000;
                    end = Math.Max(end, ms);
                }
                events.Add((end, RecordingFormat.Nmea, line));
            }
        }

        if (udpLog is not null)
        {
            double? first = null;
            long last = 0;
            var text = File.ReadAllText(udpLog, Encoding.Latin1);
            foreach (var packet in Regex.Split(text, @"\r?\n[ \t]*\r?\n"))
            {
                if (packet.Trim().Length == 0) continue;
                var m = Regex.Match(packet, @"^time=([-+0-9.Ee]+)", RegexOptions.Multiline);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                {
                    first ??= hours;
                    last = Math.Max(last, (long)((hours - first.Value) * 3_600_000));
                }
                events.Add((last, RecordingFormat.Udp, packet.Trim()));
            }
            end = Math.Max(end, last);
        }

        if (spectateJson is not null)
        {
            // only a single snapshot is available: repeat it once per second
            var json = File.ReadAllText(spectateJson);
            for (long ms = 0; ms <= Math.Max(end, 1000); ms += 1000)
                events.Add((ms, RecordingFormat.Spectate, json));
        }

        var limit = maxDuration is { } d ? (long)d.TotalMilliseconds : long.MaxValue;
        using var writer = new StreamWriter(output, false, Encoding.UTF8);
        writer.WriteLine($"# Condor2NMEA recording converted {DateTime.UtcNow:O}");
        var count = 0;
        foreach (var e in events.Where(e => e.Ms <= limit).OrderBy(e => e.Ms))
        {
            writer.WriteLine($"{e.Ms}\t{e.Channel}\t{RecordingFormat.Escape(e.Payload)}");
            count++;
        }
        return count;
    }
}
