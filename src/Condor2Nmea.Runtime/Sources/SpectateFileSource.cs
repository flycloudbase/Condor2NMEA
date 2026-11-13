using System.Text;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>
/// Polls Condor 3's Logs\Spectate.json. Condor rewrites the file continuously, so it is opened
/// with full sharing and a half written file is simply retried on the next poll.
/// </summary>
public sealed class SpectateFileSource : InputSource
{
    private const long MaxSize = 4 * 1024 * 1024;
    private readonly string _path;
    private readonly TimeSpan _interval;

    public SpectateFileSource(string path, int intervalMs, IInputSink sink)
        : base("Spectate (Datei)", sink)
    {
        _path = path;
        _interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 100, 10_000));
    }

    public override async Task RunAsync(CancellationToken token)
    {
        DateTime lastWrite = default;
        long lastLength = -1;
        var badReads = 0;
        Sink.Log(MonitorDirection.Info, Stats.Name, $"Beobachte {_path}");

        while (!token.IsCancellationRequested)
        {
            try
            {
                var fi = new FileInfo(_path);
                if (!fi.Exists)
                {
                    Stats.SetState(ChannelState.Waiting, "Datei existiert (noch) nicht – Multiplayer-Flug starten");
                }
                else if (fi.LastWriteTimeUtc != lastWrite || fi.Length != lastLength)
                {
                    var json = await ReadShared(fi.FullName, token).ConfigureAwait(false);
                    if (SpectateParser.TryParse(json) is null)
                    {
                        // probably caught Condor while writing – retry soon
                        if (++badReads >= 5)
                        {
                            Stats.OnError("Ungültiges JSON");
                            badReads = 0;
                        }
                        await DelaySafe(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
                        continue;
                    }
                    badReads = 0;
                    lastWrite = fi.LastWriteTimeUtc;
                    lastLength = fi.Length;
                    var now = DateTime.UtcNow;
                    var sampleTime = lastWrite > now || lastWrite < now.AddSeconds(-5) ? now : lastWrite;
                    Stats.OnMessage(now);
                    Sink.OnSpectateJson(json, sampleTime);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Stats.OnError(ex.Message);
            }
            await DelaySafe(_interval, token).ConfigureAwait(false);
        }
        Stats.SetState(ChannelState.Stopped);
    }

    private static async Task<string> ReadShared(string path, CancellationToken token)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        if (fs.Length > MaxSize)
            throw new IOException("Spectate.json ist zu groß");
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
    }
}
