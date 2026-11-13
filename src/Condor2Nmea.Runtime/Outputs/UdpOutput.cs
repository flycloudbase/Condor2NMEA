using System.Net.Sockets;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.Runtime.Outputs;

/// <summary>Sends the NMEA stream as UDP datagrams (XCSoar port type "UDP port").</summary>
public sealed class UdpOutput : NmeaOutput
{
    private UdpClient? _client;

    public UdpOutput(OutputSettings settings, Action<MonitorDirection, string, string> log) : base(settings, log) { }

    public override string Connections => $"→ {Settings.Host}:{Settings.Port}";

    public override void Send(IReadOnlyList<string> sentences)
    {
        var c = _client;
        if (c is null || sentences.Count == 0)
            return;
        try
        {
            var data = Encode(sentences);
            c.Send(data, data.Length);
            Stats.OnMessage(DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            Stats.OnError(ex.Message);
        }
    }

    public override async Task RunAsync(CancellationToken token)
    {
        try
        {
            var client = new UdpClient();
            if (Settings.Host is "255.255.255.255" || Settings.Host.EndsWith(".255", StringComparison.Ordinal))
                client.EnableBroadcast = true;
            client.Connect(string.IsNullOrWhiteSpace(Settings.Host) ? "127.0.0.1" : Settings.Host, Settings.Port);
            _client = client;
            Stats.SetState(ChannelState.Waiting, $"sende an {Settings.Host}:{Settings.Port}");
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Stats.SetState(ChannelState.Error, ex.Message);
            Log(MonitorDirection.Error, Settings.Name, ex.Message);
        }
        finally
        {
            _client?.Dispose();
            _client = null;
            Stats.SetState(ChannelState.Stopped);
        }
    }
}
