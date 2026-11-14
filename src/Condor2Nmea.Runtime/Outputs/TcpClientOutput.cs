using System.Net.Sockets;
using System.Threading.Channels;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.Runtime.Outputs;

/// <summary>
/// Connects to a program that listens (XCSoar port type "TCP port", default 4353) – this is what
/// HW VSP3 does in the Condor manual. Reconnects automatically.
/// </summary>
public sealed class TcpClientOutput : NmeaOutput
{
    private volatile Channel<byte[]>? _queue;
    private volatile string _state = "nicht verbunden";

    public TcpClientOutput(OutputSettings settings, Action<MonitorDirection, string, string> log) : base(settings, log) { }

    public override string Connections => $"{Settings.Host}:{Settings.Port} – {_state}";

    public override void Send(IReadOnlyList<string> sentences)
    {
        var q = _queue;
        if (q is null || sentences.Count == 0)
            return;
        q.Writer.TryWrite(Encode(sentences));
        Stats.OnMessage(DateTime.UtcNow);
    }

    public override async Task RunAsync(CancellationToken token)
    {
        var lastError = "";
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient { NoDelay = true };
                _state = "verbinde …";
                Stats.SetState(ChannelState.Waiting, $"Verbinde mit {Settings.Host}:{Settings.Port}");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await client.ConnectAsync(Settings.Host, Settings.Port, timeout.Token).ConfigureAwait(false);
                }
                _state = "verbunden";
                lastError = "";
                Log(MonitorDirection.Info, Settings.Name, $"Verbunden mit {Settings.Host}:{Settings.Port}");
                Stats.SetState(ChannelState.Active, "verbunden");

                var stream = client.GetStream();
                var queue = CreateQueue();
                _queue = queue;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var reader = ReadIncomingAsync(stream, $"{Settings.Host}:{Settings.Port}", cts.Token);
                var writer = WriteLoop(stream, queue, cts.Token);
                await Task.WhenAny(reader, writer).ConfigureAwait(false);
                cts.Cancel();
                _queue = null;
                Log(MonitorDirection.Info, Settings.Name, "Verbindung getrennt");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _queue = null;
                var msg = ex is OperationCanceledException ? "Zeitüberschreitung" : ex.Message;
                _state = msg;
                Stats.SetState(ChannelState.Waiting, $"{Settings.Host}:{Settings.Port}: {msg}");
                if (msg != lastError)
                {
                    Log(MonitorDirection.Error, Settings.Name, msg);
                    lastError = msg;
                }
            }
            await SafeDelay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
        }
        _queue = null;
        _state = "gestoppt";
        Stats.SetState(ChannelState.Stopped);
    }

    private static async Task WriteLoop(Stream stream, Channel<byte[]> queue, CancellationToken token)
    {
        try
        {
            await foreach (var data in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                await stream.WriteAsync(data, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}
