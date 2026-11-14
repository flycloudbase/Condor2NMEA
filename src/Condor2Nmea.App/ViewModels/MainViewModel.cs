using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condor2Nmea.Core.Config;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Runtime;
using Condor2Nmea.Runtime.CondorFiles;

namespace Condor2Nmea.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxMonitorLines = 1500;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentQueue<MonitorEntry> _monitorQueue = new();
    private BridgeEngine? _engine;
    private bool _condorWasRunningAtStart;

    public MainViewModel()
    {
        Settings = LoadSettings();
        Outputs = new ObservableCollection<OutputSettings>(Settings.Outputs);
        ForwardTargets = string.Join(Environment.NewLine, Settings.Udp.ForwardTargets);
        ComPort = new ComPortViewModel(this);
        LocalAddresses = string.Join(", ", GetLocalAddresses());
        _condorWasRunningAtStart = CondorInstallation.IsCondorRunning();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        RefreshUdpIni();
    }

    // ------------------------------------------------------------------ settings

    public AppSettings Settings { get; }
    public ObservableCollection<OutputSettings> Outputs { get; }
    public ComPortViewModel ComPort { get; }

    public static Array OutputKinds { get; } = Enum.GetValues(typeof(OutputKind));
    public static Array Presets { get; } = Enum.GetValues(typeof(ProfilePreset));
    public static Array SpectateModes { get; } = Enum.GetValues(typeof(SpectateMode));

    [ObservableProperty] private string _forwardTargets;
    [ObservableProperty] private OutputSettings? _selectedOutput;
    [ObservableProperty] private string _udpIniStatus = "";
    [ObservableProperty] private string _localAddresses;

    private static AppSettings LoadSettings()
    {
        try
        {
            var s = SettingsStore.Load();
            if (!File.Exists(SettingsStore.DefaultPath) && CondorInstallation.Detect() is { } condor)
                s.Condor.InstallPath = condor;
            return s;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Einstellungen konnten nicht geladen werden, verwende Standardwerte.\n{ex.Message}",
                "Condor2NMEA", MessageBoxButton.OK, MessageBoxImage.Warning);
            return new AppSettings();
        }
    }

    /// <summary>Refreshes all bindings to <see cref="Settings"/>.</summary>
    public void NotifySettingsChanged() => OnPropertyChanged(nameof(Settings));

    public void SaveSettings()
    {
        Settings.Outputs = Outputs.ToList();
        Settings.Udp.ForwardTargets = ForwardTargets
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        SettingsStore.Save(Settings);
    }

    [RelayCommand]
    private async Task SaveAndRestart()
    {
        SaveSettings();
        StatusText = "Einstellungen gespeichert.";
        if (IsRunning)
        {
            await StopEngine();
            StartEngine();
        }
        RefreshUdpIni();
    }

    [RelayCommand]
    private void AddOutput()
    {
        var o = new OutputSettings { Name = $"Ausgang {Outputs.Count + 1}", Port = 4353 + Outputs.Count };
        Outputs.Add(o);
        SelectedOutput = o;
    }

    [RelayCommand]
    private void RemoveOutput()
    {
        if (SelectedOutput is not null)
            Outputs.Remove(SelectedOutput);
    }

    [RelayCommand]
    private void BrowseCondor()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Condor-3-Ordner wählen", InitialDirectory = Settings.Condor.InstallPath };
        if (dlg.ShowDialog() == true)
        {
            Settings.Condor.InstallPath = dlg.FolderName;
            OnPropertyChanged(nameof(Settings));
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
            MessageBox.Show($"{ini.Path} wurde geschrieben." + (backup is null ? "" : $"\nSicherung: {backup}") +
                            "\n\nCondor neu starten, damit die Änderung wirkt.", "UDP.ini", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"UDP.ini konnte nicht geschrieben werden:\n{ex.Message}\n\nEvtl. Condor2NMEA einmal als Administrator starten.",
                "UDP.ini", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshUdpIni();
    }

    private void RefreshUdpIni()
    {
        try
        {
            var ini = UdpIniFile.Load(Settings.Condor.InstallPath);
            UdpIniStatus = !ini.Exists
                ? $"{ini.Path} existiert nicht."
                : $"{ini.Path}: " + (ini.Enabled ? "aktiv" : "DEAKTIVIERT") +
                  $", Ziel {ini.Host}:{ini.Port}, Intervall {ini.SendIntervalMs} ms, ExtendedData={(ini.ExtendedData ? 1 : 0)}, ExtendedData1={(ini.ExtendedData1 ? 1 : 0)}" +
                  (ini.Port != Settings.Udp.Port ? $"  ⚠ Port passt nicht zu Condor2NMEA ({Settings.Udp.Port})" : "");
        }
        catch (Exception ex)
        {
            UdpIniStatus = ex.Message;
        }
    }

    private static IEnumerable<string> GetLocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    // ------------------------------------------------------------------ engine

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    private bool _isRunning;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _condorWarning = "";

    public string StartStopText => IsRunning ? "Stopp" : "Start";

    [RelayCommand]
    private async Task StartStop()
    {
        if (IsRunning)
            await StopEngine();
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
        _engine = new BridgeEngine(Settings);
        _engine.Monitor += e => _monitorQueue.Enqueue(e);
        _engine.Start();
        IsRunning = true;
        StatusText = "Brücke läuft.";
    }

    public async Task StopEngine()
    {
        var engine = _engine;
        _engine = null;
        IsRunning = false;
        if (engine is not null)
            await engine.StopAsync();
        Sources.Clear();
        OutputStatus.Clear();
        StatusText = "Gestoppt.";
    }

    /// <summary>Called by the COM port page after creating a pair while Condor was running.</summary>
    public void MarkPortCreated()
    {
        if (CondorInstallation.IsCondorRunning())
            _condorWasRunningAtStart = true;
    }

    // ------------------------------------------------------------------ live view

    public ObservableCollection<ChannelRow> Sources { get; } = new();
    public ObservableCollection<ChannelRow> OutputStatus { get; } = new();
    public ObservableCollection<LiveValue> LiveValues { get; } = new();
    public ObservableCollection<TrafficRow> Traffic { get; } = new();
    public ObservableCollection<MonitorRow> Monitor { get; } = new();

    [ObservableProperty] private bool _monitorPaused;
    [ObservableProperty] private bool _monitorShowIn = true;
    [ObservableProperty] private bool _monitorShowOut = true;
    [ObservableProperty] private bool _monitorShowInfo = true;
    [ObservableProperty] private string _monitorFilter = "";
    [ObservableProperty] private string _trafficInfo = "";

    [RelayCommand]
    private void ClearMonitor() => Monitor.Clear();

    private void Refresh()
    {
        var condorRunning = CondorInstallation.IsCondorRunning();
        if (!condorRunning)
            _condorWasRunningAtStart = false;
        CondorWarning = condorRunning && _condorWasRunningAtStart && ComPort.HasPairs && !(_engine?.Sources
                .Any(s => s.Stats.Name.StartsWith("NMEA") && s.Stats.Snapshot(DateTime.UtcNow).Count > 0) ?? false)
            ? "Condor lief schon, bevor der virtuelle COM-Port angelegt wurde – Condor neu starten, damit der Port in der Liste erscheint."
            : "";

        DrainMonitor();
        var engine = _engine;
        if (engine is null)
            return;
        var now = DateTime.UtcNow;

        Replace(Sources, engine.Sources.Select(s => Row("Eingang", s.Stats.Snapshot(now), s.Stats.Snapshot(now).Message)));
        Replace(OutputStatus, engine.Outputs.Select(o => Row("Ausgang", o.Stats.Snapshot(now), o.Connections)));

        var snap = engine.State.Snapshot(now);
        Replace(LiveValues, BuildLiveValues(snap));

        var targets = engine.Traffic.GetTargets(now, snap);
        var own = engine.Traffic.FindOwnShip(null, null);
        TrafficInfo = $"{engine.Traffic.PilotCount} Piloten in Spectate" +
                      (own is null ? ", eigenes Flugzeug nicht erkannt" : $", eigenes Flugzeug: {own.DisplayName} ({own.PilotName})");
        Replace(Traffic, targets.Select(t => new TrafficRow(
            t.Pilot.DisplayName, t.Pilot.PilotName, t.Pilot.Plane,
            F(t.Distance / 1000, "0.00") + " km", F(t.Bearing, "0") + "°", F(t.RelativeVertical, "+0;-0") + " m",
            t.Pilot.Speed is { } sp ? F(sp * 3.6, "0") + " km/h" : "", t.Pilot.Vario is { } v ? F(v, "+0.0;-0.0") + " m/s" : "",
            t.Pilot.PlayerStatus)));
    }

    private static ChannelRow Row(string kind, ChannelSnapshot s, string message) => new(
        kind, s.Name,
        s.State switch
        {
            ChannelState.Active => "● aktiv",
            ChannelState.Waiting => "○ wartet",
            ChannelState.Error => "✖ Fehler",
            _ => "gestoppt",
        },
        F(s.RateHz, "0.0") + " Hz", s.Count.ToString(CultureInfo.CurrentCulture),
        s.Age is { } a ? (a.TotalSeconds < 60 ? F(a.TotalSeconds, "0.0") + " s" : ">1 min") : "–",
        message + (s.Errors > 0 ? $"  ({s.Errors} Fehler)" : ""));

    private static IEnumerable<LiveValue> BuildLiveValues(FlightSnapshot s)
    {
        string Src(DataSource d) => d == DataSource.None ? "" : d.ToString();
        yield return new("Position", s.Position?.ToString() ?? "–", Src(s.PositionSource));
        yield return new("Uhrzeit (Sim)", s.TimeOfDay is { } t ? t.ToString(@"hh\:mm\:ss") : "–", "");
        yield return new("GPS-Höhe", V(s.GpsAltitude, "0", "m"), "");
        yield return new("Baro-Höhe", V(s.BaroAltitude, "0", "m"), "");
        yield return new("Höhenmesser", V(s.Altimeter, "0", ""), "UDP");
        yield return new("Grundgeschw.", V(s.GroundSpeed * 3.6, "0", "km/h"), "");
        yield return new("Track", V(s.Track, "0", "°"), "");
        yield return new("TAS", V(s.TrueAirspeed * 3.6, "0", "km/h"), "");
        yield return new("Fahrt (UDP airspeed)", V(s.Airspeed * 3.6, "0", "km/h"), "UDP");
        yield return new("Kurs", V(s.Heading, "0", "°"), "");
        yield return new("Vario (TE)", V(s.TeVario, "+0.0;-0.0", "m/s"), "");
        yield return new("Vario pneum.", V(s.PneumaticVario, "+0.0;-0.0", "m/s"), "UDP");
        yield return new("E-Vario", V(s.ElectronicVario, "+0.0;-0.0", "m/s"), "UDP");
        yield return new("Netto", V(s.NettoVario, "+0.0;-0.0", "m/s"), "UDP");
        yield return new("Integrator", V(s.Integrator, "+0.0;-0.0", "m/s"), "UDP");
        yield return new("Wind", s.WindDirection is { } wd && s.WindSpeed is { } ws ? $"{F(wd, "0")}° / {F(ws * 3.6, "0")} km/h" : "–", "");
        yield return new("Querlage / Längsneigung", $"{V(s.Bank, "0.0", "°")} / {V(s.Pitch, "0.0", "°")}", "UDP");
        yield return new("G-Last", V(s.GLoad, "0.00", "g"), "UDP");
        yield return new("Höhe über Grund", V(s.HeightAboveGround, "0", "m"), "UDP");
        yield return new("MacCready", V(s.MacCready, "0.0", "m/s"), "UDP");
        yield return new("Wasserballast", V(s.WaterBallast, "0", "kg"), "UDP");
        yield return new("Klappen", s.Flaps?.ToString(CultureInfo.CurrentCulture) ?? "–", "UDP");
        yield return new("Funkfrequenz", V(s.RadioFrequency, "0.000", "MHz"), "UDP");
        yield return new("Turbulenz", V(s.Turbulence, "0.00", ""), "UDP");
        yield return new("HUD", s.HudMessages ?? "", "UDP");
    }

    private static string V(double? v, string format, string unit) =>
        v is { } x ? (F(x, format) + " " + unit).Trim() : "–";

    private static string F(double v, string format) => v.ToString(format, CultureInfo.CurrentCulture);

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

    private void DrainMonitor()
    {
        var added = 0;
        while (_monitorQueue.TryDequeue(out var e))
        {
            if (MonitorPaused || added > 400)
                continue; // drop while paused / when flooded
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
            Monitor.Add(new MonitorRow(e.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                e.Direction switch { MonitorDirection.In => "→", MonitorDirection.Out => "←", MonitorDirection.Error => "✖", _ => "ℹ" },
                e.Channel, e.Text));
            added++;
        }
        while (Monitor.Count > MaxMonitorLines)
            Monitor.RemoveAt(0);
    }

    public async Task ShutdownAsync()
    {
        _timer.Stop();
        try { SaveSettings(); } catch (Exception) { }
        await StopEngine();
    }
}
