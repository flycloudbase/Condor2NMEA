using Condor2Nmea.Core.Config;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>Reads Condor's NMEA output from a (virtual) serial port.</summary>
public sealed class SerialNmeaSource : InputSource
{
    private readonly SerialInputSettings _settings;

    public SerialNmeaSource(SerialInputSettings settings, IInputSink sink)
        : base($"NMEA ({settings.PortName})", sink) => _settings = settings;

    public override Task RunAsync(CancellationToken token) =>
        Task.Factory.StartNew(() => Run(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .ContinueWith(_ => { }, TaskScheduler.Default);

    private void Run(CancellationToken token)
    {
        var buffer = new byte[4096];
        while (!token.IsCancellationRequested)
        {
            ISerialPortReader? port = null;
            try
            {
                Stats.SetState(ChannelState.Waiting, $"Öffne {_settings.PortName} …");
                port = SerialPortFactory.Open(_settings.PortName, _settings.BaudRate);
                Stats.SetState(ChannelState.Waiting, $"{_settings.PortName} geöffnet, warte auf Condor");
                Sink.Log(MonitorDirection.Info, Stats.Name, $"{_settings.PortName} geöffnet");
                var splitter = new LineSplitter();
                while (!token.IsCancellationRequested)
                {
                    var n = port.Read(buffer);
                    if (n <= 0)
                        continue;
                    foreach (var line in splitter.Push(buffer.AsSpan(0, n)))
                    {
                        Stats.OnMessage(DateTime.UtcNow);
                        Sink.OnNmeaLine(line);
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Stats.SetState(ChannelState.Error, ex.Message);
                Stats.OnError(ex.Message);
                Sink.Log(MonitorDirection.Error, Stats.Name, ex.Message);
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                // cancelled
            }
            finally
            {
                port?.Dispose();
            }
        }
        Stats.SetState(ChannelState.Stopped);
    }
}
