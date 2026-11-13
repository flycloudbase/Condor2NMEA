using Condor2Nmea.Core.Config;
using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;
using Condor2Nmea.Core.Traffic;
using Condor2Nmea.Runtime.CondorFiles;
using Condor2Nmea.Runtime.Outputs;
using Condor2Nmea.Runtime.Sources;

namespace Condor2Nmea.Runtime;

/// <summary>
/// Central pipeline: sources → <see cref="FlightState"/> / <see cref="TrafficTracker"/> →
/// sentence generators → outputs.
/// </summary>
public sealed class BridgeEngine : IInputSink, IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly CondorNmeaParser _nmeaParser;
    private readonly List<InputSource> _sources = new();
    private readonly List<NmeaOutput> _outputs = new();
    private readonly List<Task> _tasks = new();
    private readonly HashSet<uint> _announcedIds = new();
    private readonly FlarmSentences.Options _flarmOptions;
    private CancellationTokenSource? _cts;
    private Recorder? _recorder;
    private DateTime _lastUdpMonitor;

    public BridgeEngine(AppSettings settings, string? replayFile = null, double replaySpeed = 1.0)
    {
        _settings = SettingsStore.Clone(settings);
        ReplayFile = replayFile;
        ReplaySpeed = replaySpeed;
        _nmeaParser = new CondorNmeaParser { WindDirectionIsTo = _settings.Condor.InputWindDirectionIsTo };
        Traffic = new TrafficTracker
        {
            OwnCn = _settings.Spectate.OwnCn,
            AutoDetectOwnShip = _settings.Spectate.AutoDetectOwnShip,
            MaxRangeMeters = _settings.Spectate.MaxRangeKm * 1000,
            Extrapolate = _settings.Spectate.Extrapolate,
        };
        _flarmOptions = new FlarmSentences.Options { AppendCnToId = _settings.Spectate.AppendCnToFlarmId };
    }

    public FlightState State { get; } = new();
    public TrafficTracker Traffic { get; }
    public string? ReplayFile { get; }
    public double ReplaySpeed { get; }
    public bool IsRunning => _cts is not null;
    public string? RecordingPath => _recorder?.Path;

    public IReadOnlyList<InputSource> Sources => _sources;
    public IReadOnlyList<NmeaOutput> Outputs => _outputs;

    /// <summary>Raised from worker threads.</summary>
    public event Action<MonitorEntry>? Monitor;

    public void Start()
    {
        if (_cts is not null)
            return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (_settings.Recording.Enabled && ReplayFile is null)
        {
            var dir = string.IsNullOrWhiteSpace(_settings.Recording.Directory)
                ? Path.Combine(SettingsStore.AppDataDirectory, "recordings")
                : _settings.Recording.Directory;
            try
            {
                _recorder = new Recorder(dir);
                Log(MonitorDirection.Info, "Aufzeichnung", _recorder.Path);
            }
            catch (Exception ex)
            {
                Log(MonitorDirection.Error, "Aufzeichnung", ex.Message);
            }
        }

        _sources.Clear();
        if (ReplayFile is not null)
        {
            _sources.Add(new ReplaySource(ReplayFile, this, ReplaySpeed));
        }
        else
        {
            if (_settings.Serial.Enabled && !string.IsNullOrWhiteSpace(_settings.Serial.PortName))
                _sources.Add(new SerialNmeaSource(_settings.Serial, this));
            if (_settings.Udp.Enabled)
                _sources.Add(new UdpSource(_settings.Udp, this));
            switch (_settings.Spectate.Mode)
            {
                case SpectateMode.File:
                    var path = string.IsNullOrWhiteSpace(_settings.Spectate.FilePath)
                        ? CondorInstallation.DefaultSpectatePath(_settings.Condor.InstallPath)
                        : _settings.Spectate.FilePath;
                    _sources.Add(new SpectateFileSource(path, _settings.Spectate.PollIntervalMs, this));
                    break;
                case SpectateMode.Http:
                    _sources.Add(new SpectateHttpSource(_settings.Spectate.HttpUrl, _settings.Spectate.PollIntervalMs, this));
                    break;
            }
        }

        _outputs.Clear();
        foreach (var o in _settings.Outputs.Where(o => o.Enabled))
            _outputs.Add(NmeaOutput.Create(o, Log));

        foreach (var s in _sources)
            _tasks.Add(Task.Run(() => s.RunAsync(token), token));
        foreach (var o in _outputs)
            _tasks.Add(Task.Run(() => o.RunAsync(token), token));
        _tasks.Add(Task.Run(() => SchedulerLoop(token), token));
        Log(MonitorDirection.Info, "Engine", "gestartet");
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null)
            return;
        cts.Cancel();
        try
        {
            await Task.WhenAll(_tasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // cancellation / timeout
        }
        _tasks.Clear();
        _recorder?.Dispose();
        _recorder = null;
        _cts = null;
        cts.Dispose();
        Log(MonitorDirection.Info, "Engine", "gestoppt");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    // ------------------------------------------------------------------ inputs

    public void OnNmeaLine(string line)
    {
        line = line.Trim();
        _recorder?.Write(RecordingFormat.Nmea, line);
        Log(MonitorDirection.In, "NMEA", line);

        var parsed = NmeaLine.TryParse(line);
        if (parsed is null)
            return;
        var now = DateTime.UtcNow;
        _nmeaParser.Apply(parsed, State, now);

        // raw passthrough: forward immediately
        var batch = new[] { parsed.Raw };
        foreach (var o in _outputs)
            if (o.Profile.RawPassthrough)
                o.Send(batch);
    }

    public void OnUdpPacket(string packet)
    {
        _recorder?.Write(RecordingFormat.Udp, packet);
        var now = DateTime.UtcNow;
        UdpPacketParser.Apply(packet, State, now);
        // UDP can arrive every millisecond – show at most 2 packets per second in the monitor
        if (now - _lastUdpMonitor > TimeSpan.FromMilliseconds(500))
        {
            _lastUdpMonitor = now;
            Log(MonitorDirection.In, "UDP", packet.Replace("\r", "").Replace('\n', ' ').Trim());
        }
    }

    public void OnSpectateJson(string json, DateTime sampleTimeUtc)
    {
        _recorder?.Write(RecordingFormat.Spectate, json);
        var pilots = SpectateParser.TryParse(json);
        if (pilots is null)
            return;
        Traffic.Update(pilots, sampleTimeUtc);

        var now = DateTime.UtcNow;
        var snap = State.Snapshot(now);
        // Only a non-Spectate position may be used to identify the own glider automatically.
        var live = snap.PositionSource is DataSource.Nmea or DataSource.Udp ? snap.Position : null;
        var own = Traffic.FindOwnShip(live, snap.BaroAltitude ?? snap.GpsAltitude);
        Log(MonitorDirection.In, "Spectate",
            $"{pilots.Count} Piloten" + (own is null ? "" : $", eigenes Flugzeug: {own.DisplayName}"));

        if (own is not null && _settings.Spectate.UseAsPositionFallback)
        {
            State.ApplySpectateOwnShip(own, sampleTimeUtc);
        }
    }

    public void Log(MonitorDirection direction, string channel, string text) =>
        Monitor?.Invoke(new MonitorEntry(DateTime.Now, direction, channel, text));

    // ------------------------------------------------------------------ scheduler

    private async Task SchedulerLoop(CancellationToken token)
    {
        var rates = _settings.Rates;
        var gpsPeriod = Period(rates.GpsHz);
        var varioPeriod = Period(rates.VarioHz);
        var flarmPeriod = Period(rates.FlarmHz);
        var namesPeriod = TimeSpan.FromSeconds(Math.Max(1, rates.FlarmNamesIntervalSeconds));
        DateTime nextGps = default, nextVario = default, nextFlarm = default, nextNames = default;
        var flarmEnabled = _settings.Spectate.Mode != SpectateMode.Off || ReplayFile is not null;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var now = DateTime.UtcNow;
                var gps = now >= nextGps;
                var vario = now >= nextVario;
                var flarm = flarmEnabled && now >= nextFlarm;
                if (!gps && !vario && !flarm)
                    continue;
                if (gps) nextGps = Advance(nextGps, now, gpsPeriod);
                if (vario) nextVario = Advance(nextVario, now, varioPeriod);
                if (flarm) nextFlarm = Advance(nextFlarm, now, flarmPeriod);

                try
                {
                    Tick(now, gps, vario, flarm, ref nextNames, namesPeriod);
                }
                catch (Exception ex)
                {
                    Log(MonitorDirection.Error, "Engine", ex.ToString());
                }
                if (_recorder is not null && gps)
                    _recorder.Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static TimeSpan Period(double hz) => TimeSpan.FromSeconds(1.0 / Math.Clamp(hz, 0.1, 20));

    private static DateTime Advance(DateTime due, DateTime now, TimeSpan period)
    {
        var next = due == default ? now + period : due + period;
        return next < now ? now + period : next;
    }

    private void Tick(DateTime now, bool gps, bool vario, bool flarm, ref DateTime nextNames, TimeSpan namesPeriod)
    {
        var snap = State.Snapshot(now);

        IReadOnlyList<string> gpsLines = gps ? GpsSentences.Build(snap, now).ToList() : Array.Empty<string>();
        string? lxwp0 = null, lxwp0To = null, lxwp2 = null, pov = null;
        if (vario)
        {
            lxwp0 = LxSentences.Lxwp0(snap);
            lxwp0To = LxSentences.Lxwp0(snap, windDirectionTo: true);
            lxwp2 = LxSentences.Lxwp2(snap);
            pov = OpenVarioSentences.Pov(snap);
        }

        List<string>? flarmLines = null;
        if (flarm)
        {
            var targets = Traffic.GetTargets(now, snap);
            flarmLines = new List<string>();
            var announceAll = now >= nextNames;
            if (announceAll)
            {
                nextNames = now + namesPeriod;
                _announcedIds.Clear();
            }
            var names = new List<string>();
            foreach (var t in targets.Take(_flarmOptions.MaxTargets))
                if (_announcedIds.Add(t.HexId))
                    names.AddRange(FlarmSentences.Pflam(t));
            flarmLines.Add(FlarmSentences.Pflau(targets.Count));
            flarmLines.AddRange(FlarmSentences.Pflaa(targets, _flarmOptions));
            // names are kept separate so outputs without FlarmNames can skip them
            _pendingNames = names;
        }

        var any = false;
        foreach (var o in _outputs)
        {
            var p = o.Profile;
            var lines = new List<string>();
            if (p.Gps && !p.RawPassthrough) lines.AddRange(gpsLines);
            if (vario)
            {
                if (p.Lxwp0 && !p.RawPassthrough && (p.WindDirectionTo ? lxwp0To : lxwp0) is { } lx) lines.Add(lx);
                if (p.Lxwp2 && lxwp2 is not null) lines.Add(lxwp2);
                if (p.OpenVario && pov is not null) lines.Add(pov);
            }
            if (flarmLines is not null && p.Flarm)
            {
                if (p.FlarmNames) lines.AddRange(_pendingNames);
                lines.AddRange(flarmLines);
            }
            if (lines.Count > 0)
            {
                o.Send(lines);
                any = true;
            }
        }

        if (any && Monitor is not null)
        {
            if (gps) foreach (var l in gpsLines) Log(MonitorDirection.Out, "Ausgabe", l);
            if (vario)
            {
                if (lxwp0 is not null) Log(MonitorDirection.Out, "Ausgabe", lxwp0);
                if (pov is not null && _outputs.Any(o => o.Profile.OpenVario)) Log(MonitorDirection.Out, "Ausgabe", pov);
            }
            if (flarmLines is not null)
            {
                foreach (var l in _pendingNames) Log(MonitorDirection.Out, "Ausgabe", l);
                foreach (var l in flarmLines) Log(MonitorDirection.Out, "Ausgabe", l);
            }
        }
    }

    private List<string> _pendingNames = new();
}
