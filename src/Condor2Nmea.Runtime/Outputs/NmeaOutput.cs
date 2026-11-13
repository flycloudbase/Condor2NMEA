using System.Text;
using System.Threading.Channels;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.Runtime.Outputs;

public abstract class NmeaOutput
{
    protected NmeaOutput(OutputSettings settings, Action<MonitorDirection, string, string> log)
    {
        Settings = settings;
        Profile = settings.EffectiveProfile;
        Stats = new ChannelStats(settings.Name);
        Log = log;
    }

    public OutputSettings Settings { get; }
    public OutputProfile Profile { get; }
    public ChannelStats Stats { get; }
    protected Action<MonitorDirection, string, string> Log { get; }

    /// <summary>Human readable connection info, e.g. "2 Clients: 192.168.0.12:51234, …".</summary>
    public abstract string Connections { get; }

    public abstract Task RunAsync(CancellationToken token);

    /// <summary>Queues complete sentences (without line ends). Never blocks.</summary>
    public abstract void Send(IReadOnlyList<string> sentences);

    protected static byte[] Encode(IReadOnlyList<string> sentences)
    {
        var sb = new StringBuilder();
        foreach (var s in sentences)
            sb.Append(s).Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Bounded queue per connection: slow clients lose old data instead of blocking everything.</summary>
    protected static Channel<byte[]> CreateQueue() =>
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    protected static async Task SafeDelay(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Reads what a client sends back (e.g. XCSoar declarations) and logs it.</summary>
    protected async Task ReadIncomingAsync(Stream stream, string peer, CancellationToken token)
    {
        var buffer = new byte[1024];
        var splitter = new Sources.LineSplitter();
        while (!token.IsCancellationRequested)
        {
            var n = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (n == 0)
                return; // closed by peer
            foreach (var line in splitter.Push(buffer.AsSpan(0, n)))
                Log(MonitorDirection.In, $"{Settings.Name} ← {peer}", line);
        }
    }

    public static NmeaOutput Create(OutputSettings settings, Action<MonitorDirection, string, string> log) =>
        settings.Kind switch
        {
            OutputKind.TcpServer => new TcpServerOutput(settings, log),
            OutputKind.TcpClient => new TcpClientOutput(settings, log),
            OutputKind.Udp => new UdpOutput(settings, log),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
}
