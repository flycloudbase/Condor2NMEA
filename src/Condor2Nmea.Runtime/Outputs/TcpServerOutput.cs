using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.Runtime.Outputs;

/// <summary>
/// Listens on a TCP port; every connected client gets the NMEA stream.
/// In XCSoar choose port type "TCP client" with the IP of the Condor PC.
/// </summary>
public sealed class TcpServerOutput : NmeaOutput
{
    private readonly ConcurrentDictionary<string, Channel<byte[]>> _clients = new();

    public TcpServerOutput(OutputSettings settings, Action<MonitorDirection, string, string> log) : base(settings, log) { }

    public override string Connections =>
        _clients.IsEmpty ? $"Port {Settings.Port}, kein Client" : $"Port {Settings.Port}: " + string.Join(", ", _clients.Keys);

    public override void Send(IReadOnlyList<string> sentences)
    {
        if (_clients.IsEmpty || sentences.Count == 0)
            return;
        var data = Encode(sentences);
        foreach (var q in _clients.Values)
            q.Writer.TryWrite(data);
        Stats.OnMessage(DateTime.UtcNow);
    }

    public override async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpListener? listener = null;
            try
            {
                var bind = string.IsNullOrWhiteSpace(Settings.Host) || !IPAddress.TryParse(Settings.Host, out var ip)
                    ? IPAddress.Any
                    : ip;
                listener = new TcpListener(bind, Settings.Port);
                listener.Start();
                Stats.SetState(ChannelState.Waiting, $"Lausche auf {bind}:{Settings.Port}");
                Log(MonitorDirection.Info, Settings.Name, $"TCP-Server lauscht auf {bind}:{Settings.Port}");
                while (!token.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    _ = HandleClientAsync(client, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Stats.SetState(ChannelState.Error, ex.Message);
                Stats.OnError(ex.Message);
                Log(MonitorDirection.Error, Settings.Name, ex.Message);
                await SafeDelay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            }
            finally
            {
                listener?.Stop();
            }
        }
        Stats.SetState(ChannelState.Stopped);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        var peer = client.Client.RemoteEndPoint?.ToString() ?? "?";
        var queue = CreateQueue();
        _clients[peer] = queue;
        Log(MonitorDirection.Info, Settings.Name, $"Client verbunden: {peer}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            client.NoDelay = true;
            using (client)
            {
                var stream = client.GetStream();
                var reader = ReadIncomingAsync(stream, peer, cts.Token);
                var writer = WriteLoop(stream, queue, cts.Token);
                await Task.WhenAny(reader, writer).ConfigureAwait(false);
                cts.Cancel();
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            _clients.TryRemove(peer, out _);
            if (!token.IsCancellationRequested)
                Log(MonitorDirection.Info, Settings.Name, $"Client getrennt: {peer}");
        }
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
