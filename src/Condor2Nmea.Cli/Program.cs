using System.Globalization;
using Condor2Nmea.Core.Config;
using Condor2Nmea.Runtime;
using Condor2Nmea.Runtime.CondorFiles;
using Condor2Nmea.Runtime.Sources;
using Condor2Nmea.Runtime.VirtualCom;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var argsList = args.ToList();
string? Option(string name)
{
    var i = argsList.IndexOf(name);
    if (i < 0 || i + 1 >= argsList.Count) return null;
    var v = argsList[i + 1];
    argsList.RemoveRange(i, 2);
    return v;
}
bool Flag(string name) => argsList.Remove(name);

var settingsPath = Option("--settings");
var command = argsList.FirstOrDefault(a => !a.StartsWith("--")) ?? "run";

try
{
    switch (command)
    {
        case "run":
            return await Run();
        case "default-settings":
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new AppSettings(), SettingsStore.JsonOptions));
            return 0;
        case "convert-logs":
            return ConvertLogs();
        case "com0com":
            return await Com0com();
        case "udp-ini":
            return UdpIni();
        case "help":
        case "--help":
        case "-h":
            Usage();
            return 0;
        default:
            Usage();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fehler: {ex.Message}");
    return 1;
}

void Usage() => Console.WriteLine("""
Condor2NMEA – Condor 3 NMEA/UDP/Spectate → NMEA über TCP

  condor2nmea run [--settings datei] [--replay aufnahme.c2nrec] [--speed 1.0] [--quiet] [--out]
        Startet die Brücke (Standard-Einstellungen: %APPDATA%\Condor2NMEA\settings.json).
        --replay spielt eine Aufnahme statt der Live-Quellen ab, --out zeigt die erzeugten Sätze.
  condor2nmea default-settings
        Gibt eine Beispiel-Konfiguration aus.
  condor2nmea convert-logs [--nmea nmea.txt] [--udp udplog.txt] [--spectate Spectate.json] [--seconds N] -o aufnahme.c2nrec
        Baut aus Condor-Logs eine Wiedergabedatei.
  condor2nmea com0com list | create [COM-Nummer] | remove <index>
        Verwaltet virtuelle COM-Port-Paare (Windows, com0com muss installiert sein).
  condor2nmea udp-ini [--condor C:\Condor3] [--apply]
        Zeigt UDP.ini an bzw. aktiviert die empfohlenen Einstellungen.
""");

async Task<int> Run()
{
    var replay = Option("--replay");
    var speed = double.TryParse(Option("--speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.0;
    var quiet = Flag("--quiet");
    var showOut = Flag("--out");
    var settings = SettingsStore.Load(settingsPath);
    if (settingsPath is null && !File.Exists(SettingsStore.DefaultPath))
        Console.WriteLine($"Keine Einstellungen gefunden, verwende Standardwerte ({SettingsStore.DefaultPath}).");

    await using var engine = new BridgeEngine(settings, replay, speed);
    engine.Monitor += e =>
    {
        if (e.Direction is MonitorDirection.Info or MonitorDirection.Error)
            Console.WriteLine($"{e.Time:HH:mm:ss} [{e.Channel}] {e.Text}");
        else if (showOut && e.Direction == MonitorDirection.Out)
            Console.WriteLine($"{e.Time:HH:mm:ss.fff} > {e.Text}");
    };
    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    engine.Start();
    Console.WriteLine("Läuft – Strg+C beendet.");

    while (!done.Task.IsCompleted)
    {
        await Task.WhenAny(done.Task, Task.Delay(5000));
        if (done.Task.IsCompleted) break;
        if (!quiet) PrintStatus(engine);
        if (replay is not null && engine.Sources.All(src => src.Stats.Snapshot(DateTime.UtcNow).State == ChannelState.Stopped))
        {
            Console.WriteLine("Wiedergabe beendet.");
            break;
        }
    }
    await engine.StopAsync();
    return 0;
}

static void PrintStatus(BridgeEngine engine)
{
    var now = DateTime.UtcNow;
    foreach (var src in engine.Sources)
    {
        var st = src.Stats.Snapshot(now);
        Console.WriteLine($"  Quelle {st.Name,-28} {st.State,-8} {st.RateHz,6:F1} Hz  {st.Count,8} Nachr.  {st.Message}");
    }
    foreach (var o in engine.Outputs)
        Console.WriteLine($"  Ausgabe {o.Settings.Name,-27} {o.Connections}");
    var snap = engine.State.Snapshot(now);
    Console.WriteLine($"  Pos {snap.Position?.ToString() ?? "-"} ({snap.PositionSource})  Höhe {snap.BaroAltitude:F0} m  " +
                      $"TAS {snap.TrueAirspeed * 3.6:F0} km/h  Vario {snap.TeVario:F1} m/s  Netto {snap.NettoVario:F1}  " +
                      $"Wind {snap.WindDirection:F0}°/{snap.WindSpeed * 3.6:F0} km/h  Verkehr {engine.Traffic.PilotCount}");
}

int ConvertLogs()
{
    var output = Option("-o") ?? Option("--output") ?? "condor.c2nrec";
    var seconds = Option("--seconds");
    var count = LogConverter.Convert(Option("--nmea"), Option("--udp"), Option("--spectate"), output,
        seconds is null ? null : TimeSpan.FromSeconds(double.Parse(seconds, CultureInfo.InvariantCulture)));
    Console.WriteLine($"{count} Ereignisse nach {output} geschrieben.");
    return 0;
}

async Task<int> Com0com()
{
    var settings = SettingsStore.Load(settingsPath);
    var mgr = new Com0comManager(settings.Com0com.InstallPath);
    var sub = argsList.Skip(1).FirstOrDefault() ?? "list";
    if (!mgr.IsInstalled)
    {
        Console.WriteLine($"com0com wurde nicht gefunden. Download: {Com0comManager.DownloadUrl}");
        return 1;
    }
    switch (sub)
    {
        case "list":
            foreach (var p in await mgr.ListPairsAsync())
                Console.WriteLine($"  Paar {p.Index}: Condor → {p.CondorPort}   Condor2NMEA liest {p.AppPort}");
            Console.WriteLine("  Vorhandene COM-Ports: " + string.Join(", ", ComPortRegistry.GetPresentPorts()));
            return 0;
        case "create":
            var n = argsList.Count > 2 ? int.Parse(argsList[2], CultureInfo.InvariantCulture) : ComPortRegistry.SuggestFreeComNumber();
            var pair = await mgr.CreatePairAsync(n);
            Console.WriteLine($"Angelegt: Condor → {pair.CondorPort}, Condor2NMEA liest {pair.AppPort}");
            settings.Serial.PortName = pair.AppPort;
            settings.Serial.Enabled = true;
            SettingsStore.Save(settings, settingsPath);
            Console.WriteLine("Einstellungen aktualisiert. Condor jetzt (neu) starten und unter Setup → Options → NMEA output " + pair.CondorPort + " wählen.");
            return 0;
        case "remove":
            await mgr.RemovePairAsync(int.Parse(argsList[2], CultureInfo.InvariantCulture));
            Console.WriteLine("Entfernt.");
            return 0;
        default:
            Usage();
            return 2;
    }
}

int UdpIni()
{
    var settings = SettingsStore.Load(settingsPath);
    var condor = Option("--condor") ?? settings.Condor.InstallPath;
    var ini = UdpIniFile.Load(condor);
    if (Flag("--apply"))
    {
        ini.ApplyRecommended(settings.Udp.Port);
        var backup = ini.Save();
        Console.WriteLine($"{ini.Path} geschrieben" + (backup is null ? "" : $" (Sicherung: {backup})"));
    }
    Console.WriteLine($"{ini.Path}: {(ini.Exists ? "" : "(existiert nicht) ")}Enabled={ini.Enabled} Host={ini.Host} Port={ini.Port} " +
                      $"SendIntervalMs={ini.SendIntervalMs} ExtendedData={ini.ExtendedData} ExtendedData1={ini.ExtendedData1}");
    return 0;
}
