using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>Polls Condor's Spectate HTTP server (only available in Spectate! mode).</summary>
public sealed class SpectateHttpSource : InputSource
{
    private readonly Uri _url;
    private readonly TimeSpan _interval;

    public SpectateHttpSource(string url, int intervalMs, IInputSink sink)
        : base("Spectate (HTTP)", sink)
    {
        _url = new Uri(url);
        _interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 200, 10_000));
    }

    public override async Task RunAsync(CancellationToken token)
    {
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        Sink.Log(MonitorDirection.Info, Stats.Name, $"Frage {_url} ab");
        while (!token.IsCancellationRequested)
        {
            try
            {
                var json = await http.GetStringAsync(_url, token).ConfigureAwait(false);
                if (SpectateParser.TryParse(json) is null)
                {
                    Stats.OnError("Ungültiges JSON");
                }
                else
                {
                    var now = DateTime.UtcNow;
                    Stats.OnMessage(now);
                    Sink.OnSpectateJson(json, now);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Stats.SetState(ChannelState.Waiting, $"Keine Verbindung ({ex.Message}) – läuft Condor im Spectate!-Modus?");
            }
            await DelaySafe(_interval, token).ConfigureAwait(false);
        }
        Stats.SetState(ChannelState.Stopped);
    }
}
