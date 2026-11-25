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
        _flarmOptions = new FlarmSentences.Options { NonStandardCnInId = _settings.Spectate.NonStandardCnInFlarmId };
    }

    public FlightState State { get; } = new();
    public TrafficTracker Traffic { get; }
    public string? ReplayFile { get; }
    public double ReplaySpeed { get; }
    public bool IsRunning => _cts is not null;
    public string? RecordingPath => _recorder?.Path;

    public IReadOnlyList<InputSource> Sources => _sources;

    /// <summary>Statistics per kind of data, independent of where it came from (live or replay).</summary>
    public ChannelStats NmeaInput { get; } = new("NMEA");
    public ChannelStats UdpInput { get; } = new("UDP");
    public ChannelStats SpectateInput { get; } = new("Spectate");
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
        NmeaInput.OnMessage(DateTime.UtcNow);
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
        UdpInput.OnMessage(DateTime.UtcNow);
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
        SpectateInput.OnMessage(DateTime.UtcNow);
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
            // ICD order: {PFLAA}n, then the PFLAU heartbeat
            var alarms = ComputeAlarms(now, snap, targets);
            CurrentAlarms = alarms;
            flarmLines.AddRange(FlarmSentences.Pflaa(targets, _flarmOptions, alarms));
            var rx = Traffic.PilotCount > 0 ? Math.Max(targets.Count, Traffic.PilotCount - 1) : 0;
            var worst = targets.Where(t => alarms.GetValueOrDefault(t.HexId) > 0)
                .OrderByDescending(t => alarms[t.HexId]).ThenBy(t => t.Distance).FirstOrDefault();
            flarmLines.Add(worst is not null && snap.Track is { } ownTrack
                ? FlarmSentences.PflauAlarm(rx, GpsStatusOf(snap), alarms[worst.HexId], worst, ownTrack)
                : FlarmSentences.Pflau(rx, GpsStatusOf(snap)));
            // names are kept separate so outputs without FlarmNames can skip them
            _pendingNames = names;
        }

        List<string>? cloudbaseLines = null;
        if (_outputs.Any(o => o.Profile.Cloudbase))
            cloudbaseLines = CloudbaseLines(now, snap, vario);

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
            if (cloudbaseLines is not null && p.Cloudbase) lines.AddRange(cloudbaseLines);
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
            if (cloudbaseLines is not null)
                foreach (var l in cloudbaseLines) Log(MonitorDirection.Out, "Ausgabe", l);
            if (flarmLines is not null)
            {
                foreach (var l in _pendingNames) Log(MonitorDirection.Out, "Ausgabe", l);
                foreach (var l in flarmLines) Log(MonitorDirection.Out, "Ausgabe", l);
            }
        }
    }

    private List<string> _pendingNames = new();

    // ------------------------------------------------------------------ collision warnings

    private readonly TurnRateEstimator _ownTurn = new();
    private readonly AlarmHold _alarmHold = new();

    /// <summary>Alarm level per target id of the last FLARM cycle (for the UI).</summary>
    public IReadOnlyDictionary<uint, int> CurrentAlarms { get; private set; } = new Dictionary<uint, int>();

    private Dictionary<uint, int> ComputeAlarms(DateTime now, FlightSnapshot snap, IReadOnlyList<TrafficTarget> targets)
    {
        var result = new Dictionary<uint, int>();
        if (!_settings.Collision.Enabled || snap.GroundSpeed is not { } gs || snap.Track is not { } track ||
            GpsStatusOf(snap) != FlarmSentences.GpsStatus.Airborne)
            return result;
        var own = new AircraftMotion(0, 0, 0, gs, track, _ownTurn.Update("own", now, track), snap.TeVario ?? 0);
        foreach (var t in targets)
        {
            var level = 0;
            if (t.Pilot.Speed is { } speed && t.Pilot.Heading is { } heading)
            {
                var other = new AircraftMotion(t.RelativeNorth, t.RelativeEast, t.RelativeVertical, speed, heading,
                    t.TurnRate, t.Pilot.Vario ?? 0);
                level = CollisionPredictor.Predict(own, other).AlarmLevel;
            }
            level = _alarmHold.Apply(t.HexId, level, now);
            if (level > 0)
                result[t.HexId] = level;
        }
        return result;
    }

    /// <summary>PFLAU &lt;GPS&gt;: no fix, 3D fix on the ground, 3D fix airborne.</summary>
    private static FlarmSentences.GpsStatus GpsStatusOf(FlightSnapshot snap)
    {
        if (snap.Position is null)
            return FlarmSentences.GpsStatus.NoFix;
        var onGround = snap.WheelHeight is { } wh
            ? wh < CloudbaseSentences.OnGroundWheelHeight
            : snap.GroundSpeed is { } gs && gs < 5;
        return onGround ? FlarmSentences.GpsStatus.OnGround : FlarmSentences.GpsStatus.Airborne;
    }

    // ------------------------------------------------------------------ $PCBC (Cloudbase)

    public static string SoftwareName { get; } =
        "Condor2NMEA " + (typeof(BridgeEngine).Assembly.GetName().Version?.ToString(3) ?? "");

    private DateTime _nextCloudbaseInfo;
    private HashSet<string> _hudSeen = new(StringComparer.Ordinal);

    /// <summary>Builds the $PCBC lines due in this tick (only called when an output wants them).</summary>
    private List<string> CloudbaseLines(DateTime now, FlightSnapshot snap, bool vario)
    {
        var lines = new List<string>();
        if (vario && CloudbaseSentences.Data(snap) is { } data)
            lines.Add(data);

        if (now >= _nextCloudbaseInfo)
        {
            _nextCloudbaseInfo = now + TimeSpan.FromSeconds(Math.Max(1, _settings.Rates.CloudbaseInfoIntervalSeconds));
            var live = snap.PositionSource is DataSource.Nmea or DataSource.Udp ? snap.Position : null;
            var own = Traffic.CurrentOwnShip(live, snap.BaroAltitude ?? snap.GpsAltitude);
            var count = Traffic.PilotCount;
            lines.Add(CloudbaseSentences.Info(own, count > 0 ? count : null, SoftwareName));
        }

        // HUD messages: Condor repeats them while they are shown; report each one once
        var current = (snap.HudMessages ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var message in current)
            if (!_hudSeen.Contains(message))
                lines.Add(CloudbaseSentences.Event(snap.TimeOfDay, message));
        _hudSeen = current;
        return lines;
    }
}
