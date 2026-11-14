using System.Text;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>
/// Recording format: one line per event, "milliseconds TAB channel TAB payload" with the payload
/// escaped (\\, \n, \r, \t). Channels: NMEA, UDP, SPECTATE.
/// </summary>
public static class RecordingFormat
{
    public const string Nmea = "NMEA";
    public const string Udp = "UDP";
    public const string Spectate = "SPECTATE";

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                '\\' => @"\\",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ => ch.ToString(),
            });
        }
        return sb.ToString();
    }

    public static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => s[i] });
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}

/// <summary>Writes all raw inputs to a file for later replay and bug reports.</summary>
public sealed class Recorder : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _writer;
    private readonly DateTime _start = DateTime.UtcNow;

    public string Path { get; }

    public Recorder(string directory)
    {
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"condor-{DateTime.Now:yyyyMMdd-HHmmss}.c2nrec");
        _writer = new StreamWriter(Path, append: false, Encoding.UTF8) { AutoFlush = false };
        _writer.WriteLine($"# Condor2NMEA recording {DateTime.UtcNow:O}");
    }

    public void Write(string channel, string payload)
    {
        var ms = (long)(DateTime.UtcNow - _start).TotalMilliseconds;
        lock (_lock)
            _writer.WriteLine($"{ms}\t{channel}\t{RecordingFormat.Escape(payload)}");
    }

    public void Flush()
    {
        lock (_lock)
            _writer.Flush();
    }

    public void Dispose()
    {
        lock (_lock)
            _writer.Dispose();
    }
}

/// <summary>Plays a recording back in real time (or faster) instead of the live sources.</summary>
public sealed class ReplaySource : InputSource
{
    private readonly string _path;
    private readonly double _speed;
    private readonly bool _loop;

    public ReplaySource(string path, IInputSink sink, double speed = 1.0, bool loop = false)
        : base($"Wiedergabe ({System.IO.Path.GetFileName(path)})", sink)
    {
        _path = path;
        _speed = speed <= 0 ? 1 : speed;
        _loop = loop;
    }

    public override async Task RunAsync(CancellationToken token)
    {
        do
        {
            var start = DateTime.UtcNow;
            foreach (var line in File.ReadLines(_path))
            {
                if (token.IsCancellationRequested)
                    break;
                if (line.Length == 0 || line[0] == '#')
                    continue;
                var parts = line.Split('\t', 3);
                if (parts.Length != 3 || !long.TryParse(parts[0], out var ms))
                    continue;
                var due = start + TimeSpan.FromMilliseconds(ms / _speed);
                var wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await DelaySafe(wait, token).ConfigureAwait(false);
                var payload = RecordingFormat.Unescape(parts[2]);
                Stats.OnMessage(DateTime.UtcNow);
                switch (parts[1])
                {
                    case RecordingFormat.Nmea: Sink.OnNmeaLine(payload); break;
                    case RecordingFormat.Udp: Sink.OnUdpPacket(payload); break;
                    case RecordingFormat.Spectate: Sink.OnSpectateJson(payload, DateTime.UtcNow); break;
                }
            }
        } while (_loop && !token.IsCancellationRequested);
        Stats.SetState(ChannelState.Stopped, "Wiedergabe beendet");
    }
}
