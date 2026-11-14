namespace Condor2Nmea.Runtime.Sources;

/// <summary>Receives raw data from the sources (implemented by <see cref="BridgeEngine"/>).</summary>
public interface IInputSink
{
    void OnNmeaLine(string line);
    void OnUdpPacket(string packet);
    void OnSpectateJson(string json, DateTime sampleTimeUtc);
    void Log(MonitorDirection direction, string channel, string text);
}

public abstract class InputSource
{
    protected InputSource(string name, IInputSink sink)
    {
        Stats = new ChannelStats(name);
        Sink = sink;
    }

    public ChannelStats Stats { get; }
    protected IInputSink Sink { get; }

    public abstract Task RunAsync(CancellationToken token);

    protected static async Task DelaySafe(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
