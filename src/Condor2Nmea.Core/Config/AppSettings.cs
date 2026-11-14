namespace Condor2Nmea.Core.Config;

public sealed class AppSettings
{
    public CondorSettings Condor { get; set; } = new();
    public SerialInputSettings Serial { get; set; } = new();
    public UdpInputSettings Udp { get; set; } = new();
    public SpectateSettings Spectate { get; set; } = new();
    public RateSettings Rates { get; set; } = new();
    public List<OutputSettings> Outputs { get; set; } = new()
    {
        new OutputSettings
        {
            Name = "XCSoar (TCP-Server 4353)",
            Kind = OutputKind.TcpServer,
            Port = 4353,
            Preset = ProfilePreset.XCSoarCondor3,
            Profile = OutputProfile.FromPreset(ProfilePreset.XCSoarCondor3),
        },
    };
    public Com0comSettings Com0com { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
    /// <summary>Start the bridge automatically when the app starts.</summary>
    public bool AutoStart { get; set; } = true;
}

public sealed class CondorSettings
{
    /// <summary>Condor 3 installation folder (contains Condor.exe, Settings\, Logs\).</summary>
    public string InstallPath { get; set; } = @"C:\Condor3";
    /// <summary>LXWP0 wind direction from Condor is "blowing to" (Condor 1/2). Condor 3: false.</summary>
    public bool InputWindDirectionIsTo { get; set; }
}

public sealed class SerialInputSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Port the program reads. With com0com this is the internal side of the pair (e.g. "CNCB0"),
    /// Condor writes to the other side (e.g. "COM9").
    /// </summary>
    public string PortName { get; set; } = "CNCB0";
    public int BaudRate { get; set; } = 4800;
}

public sealed class UdpInputSettings
{
    public bool Enabled { get; set; } = true;
    public string BindAddress { get; set; } = "127.0.0.1";
    /// <summary>Must match [Connection] Port in Condor's UDP.ini.</summary>
    public int Port { get; set; } = 55278;
    /// <summary>Forward every received UDP datagram unchanged to these "host:port" targets (e.g. motion platforms).</summary>
    public List<string> ForwardTargets { get; set; } = new();
}

public enum SpectateMode
{
    Off,
    /// <summary>Read Condor\Logs\Spectate.json (works while flying multiplayer).</summary>
    File,
    /// <summary>Poll http://localhost:8080/allPilots (Condor in Spectate! mode).</summary>
    Http,
}

public sealed class SpectateSettings
{
    public SpectateMode Mode { get; set; } = SpectateMode.File;
    /// <summary>Empty = &lt;Condor&gt;\Logs\Spectate.json</summary>
    public string FilePath { get; set; } = "";
    public string HttpUrl { get; set; } = "http://localhost:8080/allPilots";
    public int PollIntervalMs { get; set; } = 1000;
    /// <summary>Competition number of the own glider. Empty = detect automatically.</summary>
    public string OwnCn { get; set; } = "";
    public bool AutoDetectOwnShip { get; set; } = true;
    public double MaxRangeKm { get; set; } = 50;
    public bool Extrapolate { get; set; } = true;
    public bool AppendCnToFlarmId { get; set; } = true;
    /// <summary>Use the own glider from Spectate as position source if NMEA is missing.</summary>
    public bool UseAsPositionFallback { get; set; } = true;
}

public sealed class RateSettings
{
    public double GpsHz { get; set; } = 1;
    public double VarioHz { get; set; } = 2;
    public double FlarmHz { get; set; } = 1;
    /// <summary>$PFLAM name messages for every target are repeated this often.</summary>
    public double FlarmNamesIntervalSeconds { get; set; } = 10;
}

public enum OutputKind
{
    /// <summary>Listen on a port, any number of clients (XCSoar port type "TCP client").</summary>
    TcpServer,
    /// <summary>Connect to a listening program (XCSoar port type "TCP port").</summary>
    TcpClient,
    /// <summary>Send datagrams to host:port (XCSoar port type "UDP port").</summary>
    Udp,
}

public sealed class OutputSettings
{
    public string Name { get; set; } = "Ausgang";
    public bool Enabled { get; set; } = true;
    public OutputKind Kind { get; set; } = OutputKind.TcpServer;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 4353;
    public ProfilePreset Preset { get; set; } = ProfilePreset.XCSoarCondor3;
    public OutputProfile Profile { get; set; } = OutputProfile.FromPreset(ProfilePreset.XCSoarCondor3);

    /// <summary>The profile actually used: the preset, or <see cref="Profile"/> for <see cref="ProfilePreset.Custom"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public OutputProfile EffectiveProfile
    {
        get
        {
            if (Preset == ProfilePreset.Custom)
                return Profile.Clone();
            var p = OutputProfile.FromPreset(Preset);
            p.WindDirectionTo = Profile.WindDirectionTo;
            return p;
        }
    }
}

public sealed class Com0comSettings
{
    /// <summary>Folder containing setupc.exe. Empty = auto-detect.</summary>
    public string InstallPath { get; set; } = "";
}

public sealed class RecordingSettings
{
    public bool Enabled { get; set; }
    /// <summary>Empty = %APPDATA%\Condor2NMEA\recordings</summary>
    public string Directory { get; set; } = "";
}
