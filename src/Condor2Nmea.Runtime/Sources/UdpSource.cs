using System.Net;
using System.Net.Sockets;
using System.Text;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>Receives Condor's generic UDP output ("UDP.ini") and optionally forwards it.</summary>
public sealed class UdpSource : InputSource
{
    private readonly UdpInputSettings _settings;

    public UdpSource(UdpInputSettings settings, IInputSink sink)
        : base($"UDP (:{settings.Port})", sink) => _settings = settings;

    public override async Task RunAsync(CancellationToken token)
    {
        var forward = new List<IPEndPoint>();
        foreach (var target in _settings.ForwardTargets)
        {
            if (IPEndPoint.TryParse(target.Trim(), out var ep) && ep.Port != 0)
                forward.Add(ep);
            else
                Sink.Log(MonitorDirection.Error, Stats.Name, $"Ungültiges Weiterleitungsziel: {target}");
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var bind = IPAddress.TryParse(_settings.BindAddress, out var ip) ? ip : IPAddress.Loopback;
                using var udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(bind, _settings.Port));
                using var sender = forward.Count > 0 ? new UdpClient() : null;
                Stats.SetState(ChannelState.Waiting, $"Lausche auf {bind}:{_settings.Port}");
                Sink.Log(MonitorDirection.Info, Stats.Name, $"Lausche auf {bind}:{_settings.Port}");

                while (!token.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
                    Stats.OnMessage(DateTime.UtcNow);
                    var text = Encoding.Latin1.GetString(result.Buffer);
                    Sink.OnUdpPacket(text);
                    if (sender is not null)
                        foreach (var ep in forward)
                            await sender.SendAsync(result.Buffer, ep, token).ConfigureAwait(false);
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
                Sink.Log(MonitorDirection.Error, Stats.Name, ex.Message);
                await DelaySafe(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            }
        }
        Stats.SetState(ChannelState.Stopped);
    }
}
