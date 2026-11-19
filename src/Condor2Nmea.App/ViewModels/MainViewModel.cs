using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condor2Nmea.App.Controls;
using Condor2Nmea.Core.Config;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Runtime;
using Condor2Nmea.Runtime.CondorFiles;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace Condor2Nmea.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxMonitorLines = 1500;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentQueue<MonitorEntry> _monitorQueue = new();
    private readonly string? _replayFile;
    private readonly bool _isolated;
    private BridgeEngine? _engine;
    private bool _condorRunningBeforePort;
    private int _tick;

    public MainViewModel(string? replayFile = null, bool isolated = false)
    {
        _replayFile = replayFile;
        _isolated = isolated;
        Settings = LoadSettings();
        Outputs = new ObservableCollection<OutputEditor>(Settings.Outputs.Select(o => new OutputEditor(o)));
        _forwardTargets = string.Join(Environment.NewLine, Settings.Udp.ForwardTargets);
        LocalAddresses = GetLocalAddresses().ToList();
        ComPort = new ComPortViewModel(this);

        SourceCards = new ObservableCollection<SourceCard>
        {
            new("Condor NMEA", SymbolRegular.SerialPort24, "Position, Höhe, Fahrt, Vario und Wind über den virtuellen COM-Port"),
            new("Condor UDP", SymbolRegular.Gauge24, "Netto, Lage, G-Last, MacCready und mehr aus UDP.ini"),
            new("Multiplayer", SymbolRegular.People24, "Andere Segelflugzeuge aus Spectate.json als FLARM-Verkehr"),
        };

        Instruments = new ObservableCollection<InstrumentTile>
        {
            new("Höhe", "m", SymbolRegular.ArrowTrendingLines24),
            new("Fahrt (TAS)", "km/h", SymbolRegular.TopSpeed24),
            new("Vario", "m/s", SymbolRegular.ArrowSwap24),
            new("Netto", "m/s", SymbolRegular.DataLine24),
            new("Wind", "", SymbolRegular.Navigation24),
            new("Grundgeschw.", "km/h", SymbolRegular.Location24),
            new("MacCready", "m/s", SymbolRegular.Flash24),
            new("Verkehr", "Flugzeuge", SymbolRegular.People24),
        };

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        RefreshUdpIni();
        Refresh();
    }

    // ================================================================== settings

    public AppSettings Settings { get; }
    public ObservableCollection<OutputEditor> Outputs { get; }
    public ComPortViewModel ComPort { get; }
    public IReadOnlyList<string> LocalAddresses { get; }
    public string PrimaryAddress => LocalAddresses.FirstOrDefault() ?? "IP-Adresse dieses PCs";

    /// <summary>True when the user has never saved settings (first start → show setup).</summary>
    public bool IsFirstStart { get; private set; }

    public static IReadOnlyList<Option<SpectateMode>> SpectateModeOptions { get; } = new[]
    {
        new Option<SpectateMode>(SpectateMode.File, "Datei Spectate.json (beim Multiplayer-Flug)"),
        new Option<SpectateMode>(SpectateMode.Http, "HTTP-Server (Condor im Spectate!-Modus)"),
        new Option<SpectateMode>(SpectateMode.Off, "Aus"),
    };

    public static IReadOnlyList<Option<double>> RadarRangeOptions { get; } = new[]
    {
        new Option<double>(2, "2 km"), new Option<double>(5, "5 km"), new Option<double>(10, "10 km"),
        new Option<double>(25, "25 km"), new Option<double>(50, "50 km"),
    };

    [ObservableProperty] private string _forwardTargets;
    [ObservableProperty] private string _udpIniStatus = "";
    [ObservableProperty] private bool _udpIniReady;
    [ObservableProperty] private string _saveStatus = "";

    private AppSettings LoadSettings()
    {
        if (_isolated)
            return new AppSettings();
        try
        {
            IsFirstStart = !File.Exists(SettingsStore.DefaultPath);
            var s = SettingsStore.Load();
            if (IsFirstStart && CondorInstallation.Detect() is { } condor)
                s.Condor.InstallPath = condor;
            return s;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Einstellungen konnten nicht geladen werden, es werden Standardwerte verwendet.\n{ex.Message}",
                "Condor2NMEA", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new AppSettings();
        }
    }

    public void SaveSettings()
    {
        Settings.Outputs = Outputs.Select(o => o.Settings).ToList();
        Settings.Udp.ForwardTargets = ForwardTargets
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!_isolated)
            SettingsStore.Save(Settings);
        IsFirstStart = false;
    }

    /// <summary>Refreshes all bindings to <see cref="Settings"/>.</summary>
    public void NotifySettingsChanged() => OnPropertyChanged(nameof(Settings));

    [RelayCommand]
    private async Task SaveAndApply()
    {
        try
        {
            SaveSettings();
            if (IsRunning)
            {
                await StopEngine();
                StartEngine();
            }
            RefreshUdpIni();
            SaveStatus = $"Gespeichert um {DateTime.Now:HH:mm:ss}" + (IsRunning ? " – Brücke neu gestartet." : ".");
        }
        catch (Exception ex)
        {
            SaveStatus = "Speichern fehlgeschlagen: " + ex.Message;
        }
    }

    [RelayCommand]
    private void AddOutput()
    {
        var used = Outputs.Select(o => o.Settings.Port).ToHashSet();
        var port = 4353;
        while (used.Contains(port)) port++;
        Outputs.Add(new OutputEditor(new OutputSettings { Name = $"Ausgang {Outputs.Count + 1}", Port = port }));
    }

    [RelayCommand]
    private void RemoveOutput(OutputEditor? output)
    {
        if (output is not null)
            Outputs.Remove(output);
    }

    [RelayCommand]
    private void BrowseCondor()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Condor-3-Ordner wählen" };
        if (Directory.Exists(Settings.Condor.InstallPath))
            dlg.InitialDirectory = Settings.Condor.InstallPath;
        if (dlg.ShowDialog() == true)
        {
            Settings.Condor.InstallPath = dlg.FolderName;
            NotifySettingsChanged();
            RefreshUdpIni();
        }
    }

    [RelayCommand]
    private void ConfigureUdpIni()
    {
        try
        {
            var ini = UdpIniFile.Load(Settings.Condor.InstallPath);
            ini.ApplyRecommended(Settings.Udp.Port);
            var backup = ini.Save();
            RefreshUdpIni();
            MessageBox.Show($"Die Condor-UDP-Ausgabe ist eingerichtet.\n\n{ini.Path}" +
                            (backup is null ? "" : $"\nSicherung: {backup}") +
                            "\n\nFalls Condor gerade läuft: Condor neu starten.",
                "UDP.ini", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Keine Schreibrechte im Condor-Ordner. Condor2NMEA bitte einmal mit „Als Administrator ausführen“ starten.",
                "UDP.ini", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"UDP.ini konnte nicht geschrieben werden:\n{ex.Message}", "UDP.ini", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void RefreshUdpIni()
    {
        try
        {
            if (!Directory.Exists(Settings.Condor.InstallPath))
            {
                UdpIniReady = false;
                UdpIniStatus = $"Condor-Ordner „{Settings.Condor.InstallPath}“ nicht gefunden – bitte in den Einstellungen wählen.";
                return;
            }
            var ini = UdpIniFile.Load(Settings.Condor.InstallPath);
            UdpIniReady = ini.Exists && ini.Enabled && ini.Port == Settings.Udp.Port && ini.ExtendedData && ini.ExtendedData1;
            UdpIniStatus = !ini.Exists
                ? "UDP.ini existiert noch nicht."
                : !ini.Enabled
                    ? "Die UDP-Ausgabe von Condor ist ausgeschaltet."
                    : ini.Port != Settings.Udp.Port
                        ? $"Condor sendet an Port {ini.Port}, Condor2NMEA lauscht auf {Settings.Udp.Port}."
                        : UdpIniReady
                            ? $"Eingerichtet: Condor sendet alle {ini.SendIntervalMs} ms an {ini.Host}:{ini.Port}."
                            : "Aktiv, aber ohne erweiterte Daten (MacCready, Wasser, Klappen).";
        }
        catch (Exception ex)
        {
            UdpIniReady = false;
            UdpIniStatus = ex.Message;
        }
    }

    private static IEnumerable<string> GetLocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) &&
                            !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    [RelayCommand]
    private static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        try { Clipboard.SetText(text); } catch (Exception) { }
    }

    // ================================================================== engine

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopText), nameof(StartStopIcon))]
    private bool _isRunning;

    public string StartStopText => IsRunning ? "Stoppen" : "Starten";
    public SymbolRegular StartStopIcon => IsRunning ? SymbolRegular.Stop24 : SymbolRegular.Play24;

    [RelayCommand]
    private async Task StartStop()
    {
        if (IsRunning)
        {
            await StopEngine();
        }
        else
        {
            SaveSettings();
            StartEngine();
        }
    }

    public void StartEngine()
    {
        if (_engine is not null)
            return;
        _engine = new BridgeEngine(Settings, _replayFile);
        _engine.Monitor += e => _monitorQueue.Enqueue(e);
        try
        {
            _engine.Start();
        }
        catch (Exception ex)
        {
            _engine = null;
            MessageBox.Show(ex.Message, "Start fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        IsRunning = true;
        RebuildOutputCards();
        Refresh();
    }

    public async Task StopEngine()
    {
        var engine = _engine;
        _engine = null;
        IsRunning = false;
        if (engine is not null)
            await engine.StopAsync();
        Refresh();
    }

    /// <summary>Called after the COM port was created while Condor was already running.</summary>
    public void MarkPortCreated()
    {
        if (CondorInstallation.IsCondorRunning())
            _condorRunningBeforePort = true;
    }

    public async Task ShutdownAsync()
    {
        _timer.Stop();
        try { SaveSettings(); } catch (Exception) { }
        await StopEngine();
    }

    // ================================================================== dashboard

    public ObservableCollection<SourceCard> SourceCards { get; }
    public ObservableCollection<OutputCard> OutputCards { get; } = new();
    public ObservableCollection<InstrumentTile> Instruments { get; }

    [ObservableProperty] private string _heroTitle = "";
    [ObservableProperty] private string _heroText = "";
    [ObservableProperty] private Brush _heroBrush = HealthBrushes.For(Health.Off);
    [ObservableProperty] private string _condorWarning = "";
    [ObservableProperty] private bool _hasCondorWarning;
    [ObservableProperty] private string _positionText = "";

    // setup state
    [ObservableProperty] private bool _condorDataReceived;
    [ObservableProperty] private bool _deviceConnected;
    [ObservableProperty] private bool _condorRunning;

    private void RebuildOutputCards()
    {
        OutputCards.Clear();
        foreach (var o in _engine?.Outputs ?? Array.Empty<Runtime.Outputs.NmeaOutput>())
            OutputCards.Add(new OutputCard(o.Settings.Name));
    }

    private void Refresh()
    {
        _tick++;
        // process list lookups are not free – every 2 s is enough
        if (_tick % 4 == 1)
        {
            CondorRunning = CondorInstallation.IsCondorRunning();
            if (!CondorRunning)
                _condorRunningBeforePort = false;
        }

        DrainMonitor();
        var now = DateTime.UtcNow;
        var engine = _engine;
        var snap = engine?.State.Snapshot(now) ?? new FlightSnapshot { Taken = now };

        UpdateSources(engine, now);
        UpdateOutputs(engine, now);
        UpdateInstruments(engine, snap, now);
        UpdateTraffic(engine, snap, now);
        UpdateHero(engine, now);
    }

    private void UpdateSources(BridgeEngine? engine, DateTime now)
    {
        void Apply(SourceCard card, ChannelStats? stats, Func<Runtime.Sources.InputSource, bool> match, bool enabled, string disabledText)
        {
            if (engine is null)
            {
                card.Set(Health.Off, "Gestoppt", enabled ? "" : disabledText, "");
                return;
            }
            if (!enabled && engine.ReplayFile is null)
            {
                card.Set(Health.Off, "Aus", disabledText, "");
                return;
            }
            var s = stats!.Snapshot(now);
            var source = engine.Sources.FirstOrDefault(match);
            var sourceSnap = source?.Stats.Snapshot(now);
            var receiving = s.Age is { } age && age < TimeSpan.FromSeconds(5);
            var rate = receiving ? $"{s.RateHz:0.#} Hz" : "";
            if (receiving)
                card.Set(Health.Good, "Empfängt Daten", sourceSnap?.Message is { Length: > 0 } m && sourceSnap.State != ChannelState.Active ? m : $"{s.Count:N0} Nachrichten", rate);
            else if (sourceSnap?.State == ChannelState.Error)
                card.Set(Health.Error, "Fehler", sourceSnap.Message, "");
            else
                card.Set(Health.Waiting, "Wartet", sourceSnap?.Message is { Length: > 0 } msg ? msg : "Noch keine Daten – läuft Condor?", "");
        }

        Apply(SourceCards[0], engine?.NmeaInput, s => s is Runtime.Sources.SerialNmeaSource, Settings.Serial.Enabled, "NMEA-Eingang ist ausgeschaltet");
        Apply(SourceCards[1], engine?.UdpInput, s => s is Runtime.Sources.UdpSource, Settings.Udp.Enabled, "UDP-Eingang ist ausgeschaltet");
        Apply(SourceCards[2], engine?.SpectateInput, s => s is Runtime.Sources.SpectateFileSource or Runtime.Sources.SpectateHttpSource,
            Settings.Spectate.Mode != SpectateMode.Off, "Multiplayer-Verkehr ist ausgeschaltet");

        var nmea = engine?.NmeaInput.Snapshot(now);
        CondorDataReceived = nmea?.Age is { } a && a < TimeSpan.FromSeconds(5) ||
                             engine?.UdpInput.Snapshot(now).Age is { } u && u < TimeSpan.FromSeconds(5);

        var warn = CondorRunning && _condorRunningBeforePort && !(nmea?.Count > 0);
        CondorWarning = warn
            ? "Condor lief schon, bevor der virtuelle COM-Port angelegt wurde. Bitte Condor neu starten, damit der Port in der Liste erscheint."
            : "";
        HasCondorWarning = warn;
    }

    private void UpdateOutputs(BridgeEngine? engine, DateTime now)
    {
        if (engine is null)
        {
            OutputCards.Clear();
            DeviceConnected = false;
            return;
        }
        var connected = false;
        for (var i = 0; i < engine.Outputs.Count && i < OutputCards.Count; i++)
        {
            var o = engine.Outputs[i];
            var card = OutputCards[i];
            var snap = o.Stats.Snapshot(now);
            var clients = o.ClientCount;
            connected |= clients > 0;
            var profile = OutputEditor.PresetOptions.FirstOrDefault(p => p.Value == o.Settings.Preset)?.Label ?? "";
            switch (o.Settings.Kind)
            {
                case OutputKind.TcpServer:
                    card.Address = $"{PrimaryAddress} : {o.Settings.Port}";
                    card.State = snap.State == ChannelState.Error ? snap.Message
                        : clients == 0 ? "Wartet auf Verbindung" : clients == 1 ? "1 Gerät verbunden" : $"{clients} Geräte verbunden";
                    card.Instruction = $"Im Flugcomputer als „TCP-Client“ mit {PrimaryAddress}, Port {o.Settings.Port} verbinden.";
                    break;
                case OutputKind.TcpClient:
                    card.Address = $"→ {o.Settings.Host} : {o.Settings.Port}";
                    card.State = clients > 0 ? "Verbunden" : "Verbinde … (" + o.Connections.Split('–').Last().Trim() + ")";
                    card.Instruction = $"Im Flugcomputer einen „TCP-Port“ {o.Settings.Port} öffnen (Server).";
                    break;
                default:
                    card.Address = $"→ {o.Settings.Host} : {o.Settings.Port} (UDP)";
                    card.State = snap.State == ChannelState.Error ? snap.Message : "Sendet";
                    card.Instruction = $"Im Flugcomputer einen „UDP-Port“ {o.Settings.Port} öffnen.";
                    break;
            }
            card.Instruction += "  Profil: " + profile;
            card.StatusBrush = HealthBrushes.For(snap.State == ChannelState.Error ? Health.Error
                : clients != 0 ? Health.Good : Health.Waiting);
        }
        DeviceConnected = connected;
    }

    private void UpdateInstruments(BridgeEngine? engine, FlightSnapshot s, DateTime now)
    {
        string F(double? v, string fmt) => v is { } x ? x.ToString(fmt, CultureInfo.CurrentCulture) : "–";
        Instruments[0].Value = F(s.BaroAltitude ?? s.GpsAltitude, "N0");
        Instruments[1].Value = F(s.TrueAirspeed * 3.6, "0");
        Instruments[2].Value = F(s.TeVario, "+0.0;-0.0;0.0");
        Instruments[3].Value = F(s.NettoVario, "+0.0;-0.0;0.0");
        Instruments[4].Value = s.WindDirection is { } wd && s.WindSpeed is { } ws ? $"{wd:0}° / {ws * 3.6:0}" : "–";
        Instruments[4].Unit = s.WindDirection is null ? "" : "km/h";
        Instruments[5].Value = F(s.GroundSpeed * 3.6, "0");
        Instruments[6].Value = F(s.MacCready, "0.0");
        Instruments[7].Value = engine is null ? "–" : engine.Traffic.GetTargets(now, s).Count.ToString(CultureInfo.CurrentCulture);
        PositionText = s.Position is { } p
            ? $"{Math.Abs(p.Latitude):0.0000}° {(p.Latitude >= 0 ? "N" : "S")}  {Math.Abs(p.Longitude):0.0000}° {(p.Longitude >= 0 ? "E" : "W")}" +
              (s.TimeOfDay is { } t ? $"   ·   Sim-Zeit {t:hh\\:mm\\:ss}" : "") +
              (s.PositionSource == DataSource.Spectate ? "   ·   Position aus Spectate (kein NMEA)" : "")
            : "Keine Position";
    }

    private void UpdateHero(BridgeEngine? engine, DateTime now)
    {
        if (engine is null)
        {
            HeroTitle = "Gestoppt";
            HeroText = "Die Brücke ist angehalten. Mit „Starten“ beginnt Condor2NMEA, Condor-Daten zu empfangen und weiterzugeben.";
            HeroBrush = HealthBrushes.For(Health.Off);
            return;
        }
        if (!CondorDataReceived)
        {
            HeroTitle = "Warte auf Condor";
            HeroText = CondorRunning
                ? "Condor läuft, sendet aber noch keine Daten. Ist ein Flug gestartet und die NMEA-Ausgabe auf den virtuellen COM-Port gestellt?"
                : "Starte Condor und einen Flug – die Daten erscheinen dann automatisch.";
            HeroBrush = HealthBrushes.For(Health.Waiting);
            return;
        }
        if (!DeviceConnected)
        {
            HeroTitle = "Daten kommen an";
            HeroText = "Condor sendet. Jetzt den Flugcomputer verbinden (siehe Ausgänge unten).";
            HeroBrush = HealthBrushes.For(Health.Waiting);
            return;
        }
        HeroTitle = "Alles verbunden";
        HeroText = "Condor-Daten werden empfangen und an den Flugcomputer weitergegeben. Guten Flug!";
        HeroBrush = HealthBrushes.For(Health.Good);
    }

    // ================================================================== traffic

    public ObservableCollection<TrafficRow> Traffic { get; } = new();
    [ObservableProperty] private IReadOnlyList<RadarTarget> _radarTargets = Array.Empty<RadarTarget>();
    [ObservableProperty] private double _radarRangeKm = 10;
    [ObservableProperty] private string _trafficInfo = "Brücke ist gestoppt.";
    [ObservableProperty] private bool _hasTraffic;

    /// <summary>Choices for the own glider: "automatic" plus every pilot in the session.</summary>
    public ObservableCollection<Option<string>> OwnShipOptions { get; } = new() { new Option<string>("", AutoOwnShipLabel) };
    private const string AutoOwnShipLabel = "Automatisch erkennen";
    private string _ownShipOptionsKey = "";
    private bool _rebuildingOwnShipOptions;

    /// <summary>
    /// Own glider chosen by the user ("" = automatic). Takes effect immediately and is saved; the same value as
    /// „Eigene Wettbewerbskennung“ in the settings.
    /// </summary>
    public string SelectedOwnShip
    {
        get => Settings.Spectate.OwnCn;
        set
        {
            if (_rebuildingOwnShipOptions || value is null || value == Settings.Spectate.OwnCn)
                return;
            Settings.Spectate.OwnCn = value;
            if (_engine is not null)
                _engine.Traffic.OwnCn = value;
            try { SaveSettings(); } catch (Exception) { /* keeps working, saved on exit */ }
            OnPropertyChanged();
            NotifySettingsChanged();
            Refresh();
        }
    }

    private void UpdateOwnShipOptions(BridgeEngine? engine)
    {
        var pilots = engine?.Traffic.Pilots ?? Array.Empty<Core.Parsing.SpectatePilot>();
        var options = new List<Option<string>> { new("", AutoOwnShipLabel) };
        foreach (var p in pilots.OrderBy(p => p.Cn, StringComparer.OrdinalIgnoreCase))
        {
            var key = Core.Traffic.TrafficTracker.SelectionKey(p);
            if (key.Length == 0 || options.Any(o => o.Value == key))
                continue;
            var label = string.Join("  ·  ", new[] { p.Cn.Length > 0 ? p.Cn : p.Registration, p.PilotName, p.Plane }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            options.Add(new Option<string>(key, label));
        }
        // keep a manual choice visible even when that pilot is not (yet) in the session
        var chosen = Settings.Spectate.OwnCn;
        if (chosen.Length > 0 && options.All(o => !string.Equals(o.Value, chosen, StringComparison.OrdinalIgnoreCase)))
            options.Add(new Option<string>(chosen, $"{chosen}  ·  derzeit nicht im Rennen"));

        var signature = string.Join("|", options.Select(o => o.Value + "=" + o.Label));
        if (signature == _ownShipOptionsKey)
            return;
        _ownShipOptionsKey = signature;
        _rebuildingOwnShipOptions = true;
        try
        {
            OwnShipOptions.Clear();
            foreach (var o in options)
                OwnShipOptions.Add(o);
        }
        finally
        {
            _rebuildingOwnShipOptions = false;
        }
        OnPropertyChanged(nameof(SelectedOwnShip));
    }

    /// <summary>
    /// Compares the own altitude from Condor's NMEA with the own glider's Spectate altitude. A large difference means
    /// the wrong pilot is taken as own glider.
    /// </summary>
    private static string OwnAltitudeCheck(Core.Parsing.SpectatePilot own, FlightSnapshot snap)
    {
        var nmea = snap.PositionSource is DataSource.Nmea or DataSource.Udp ? snap.GpsAltitude ?? snap.BaroAltitude : null;
        if (nmea is not { } n || own.Altitude is not { } sp)
            return "";
        var text = $" · Höhe Condor {n.ToString("N0", CultureInfo.CurrentCulture)} m, Spectate {sp.ToString("N0", CultureInfo.CurrentCulture)} m";
        return Math.Abs(n - sp) > 150 ? text + " – passt nicht, ist das wirklich dein Flugzeug?" : text;
    }

    private void UpdateTraffic(BridgeEngine? engine, FlightSnapshot snap, DateTime now)
    {
        UpdateOwnShipOptions(engine);
        if (engine is null)
        {
            Traffic.Clear();
            RadarTargets = Array.Empty<RadarTarget>();
            HasTraffic = false;
            TrafficInfo = "Brücke ist gestoppt.";
            return;
        }
        var targets = engine.Traffic.GetTargets(now, snap);
        var live = snap.PositionSource is DataSource.Nmea or DataSource.Udp ? snap.Position : null;
        var own = engine.Traffic.CurrentOwnShip(live, snap.BaroAltitude ?? snap.GpsAltitude);
        var manual = Settings.Spectate.OwnCn.Length > 0;
        TrafficInfo = engine.Traffic.PilotCount == 0
            ? "Keine Multiplayer-Daten. Verkehr erscheint, sobald ein Online-Flug läuft."
            : $"{engine.Traffic.PilotCount} Piloten im Rennen · " + (own is null
                ? manual ? "das gewählte eigene Flugzeug ist nicht im Rennen" : "eigenes Flugzeug nicht erkannt – bitte oben auswählen"
                : $"eigenes Flugzeug: {own.DisplayName} {own.PilotName} ({(manual ? "manuell gewählt" : "automatisch erkannt")})" +
                  OwnAltitudeCheck(own, snap));
        HasTraffic = targets.Count > 0;
        RadarTargets = targets.Select(t => new RadarTarget(t.RelativeNorth, t.RelativeEast, t.RelativeVertical,
            t.Pilot.DisplayName, t.Pilot.Heading)).ToList();
        string F(double v, string f) => v.ToString(f, CultureInfo.CurrentCulture);
        Replace(Traffic, targets.Select(t => new TrafficRow(
            t.Pilot.DisplayName, t.Pilot.PilotName, t.Pilot.Plane,
            F(t.Distance / 1000, "0.0") + " km", F(t.Bearing, "0") + "°", F(t.RelativeVertical, "+0;-0;0") + " m",
            t.Altitude is { } alt ? F(alt, "N0") + " m" : "",
            t.Pilot.Speed is { } sp ? F(sp * 3.6, "0") + " km/h" : "", t.Pilot.Vario is { } v ? F(v, "+0.0;-0.0;0.0") + " m/s" : "",
            t.Pilot.PlayerStatus)));
    }

    // ================================================================== monitor

    public ObservableCollection<MonitorRow> Monitor { get; } = new();
    [ObservableProperty] private bool _monitorPaused;
    [ObservableProperty] private bool _monitorShowIn = true;
    [ObservableProperty] private bool _monitorShowOut = true;
    [ObservableProperty] private bool _monitorShowInfo = true;
    [ObservableProperty] private string _monitorFilter = "";

    /// <summary>Raised after new monitor lines were appended (for auto scroll).</summary>
    public event Action? MonitorAppended;

    [RelayCommand]
    private void ClearMonitor() => Monitor.Clear();

    private void DrainMonitor()
    {
        var added = 0;
        while (_monitorQueue.TryDequeue(out var e))
        {
            if (MonitorPaused || added > 400)
                continue;
            var show = e.Direction switch
            {
                MonitorDirection.In => MonitorShowIn,
                MonitorDirection.Out => MonitorShowOut,
                _ => MonitorShowInfo,
            };
            if (!show)
                continue;
            if (MonitorFilter.Length > 0 &&
                !e.Text.Contains(MonitorFilter, StringComparison.OrdinalIgnoreCase) &&
                !e.Channel.Contains(MonitorFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            var (dir, health) = e.Direction switch
            {
                MonitorDirection.In => ("EIN", Health.Good),
                MonitorDirection.Out => ("AUS", Health.Off),
                MonitorDirection.Error => ("FEHLER", Health.Error),
                _ => ("INFO", Health.Waiting),
            };
            Monitor.Add(new MonitorRow(e.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), dir, e.Channel, e.Text,
                HealthBrushes.For(health)));
            added++;
        }
        while (Monitor.Count > MaxMonitorLines)
            Monitor.RemoveAt(0);
        if (added > 0)
            MonitorAppended?.Invoke();
    }

    // ================================================================== helpers

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (i < target.Count)
            {
                if (!Equals(target[i], list[i]))
                    target[i] = list[i];
            }
            else
            {
                target.Add(list[i]);
            }
        }
        while (target.Count > list.Count)
            target.RemoveAt(target.Count - 1);
    }
}
