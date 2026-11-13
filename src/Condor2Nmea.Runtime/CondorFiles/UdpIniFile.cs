namespace Condor2Nmea.Runtime.CondorFiles;

/// <summary>Reads and writes Condor's UDP.ini (manual chapter 14.1).</summary>
public sealed class UdpIniFile
{
    public string Path { get; }
    public bool Exists => File.Exists(Path);

    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 55278;
    public int SendIntervalMs { get; set; } = 1;
    public bool ExtendedData { get; set; }
    public bool ExtendedData1 { get; set; }
    public bool LogToFile { get; set; }

    private UdpIniFile(string path) => Path = path;

    /// <summary>
    /// The manual names the "Condor installation folder"; other ini files live in Condor\Settings.
    /// An existing file wins, otherwise Settings\UDP.ini is used.
    /// </summary>
    public static string Locate(string installPath)
    {
        var settings = System.IO.Path.Combine(installPath, "Settings", "UDP.ini");
        var root = System.IO.Path.Combine(installPath, "UDP.ini");
        if (File.Exists(settings)) return settings;
        if (File.Exists(root)) return root;
        return settings;
    }

    public static UdpIniFile Load(string installPath)
    {
        var f = new UdpIniFile(Locate(installPath));
        if (!f.Exists)
            return f;
        var ini = IniFile.Load(f.Path);
        f.Enabled = ini.Get("General", "Enabled") == "1";
        f.Host = ini.Get("Connection", "Host") ?? f.Host;
        f.Port = int.TryParse(ini.Get("Connection", "Port"), out var p) ? p : f.Port;
        f.SendIntervalMs = int.TryParse(ini.Get("Misc", "SendIntervalMs"), out var s) ? s : f.SendIntervalMs;
        f.ExtendedData = ini.Get("Misc", "ExtendedData") == "1";
        f.ExtendedData1 = ini.Get("Misc", "ExtendedData1") == "1";
        f.LogToFile = ini.Get("Misc", "LogToFile") == "1";
        return f;
    }

    /// <summary>Writes the file; an existing file is copied to UDP.ini.bak first.</summary>
    public string? Save()
    {
        string? backup = null;
        if (Exists)
        {
            backup = Path + ".bak";
            File.Copy(Path, backup, overwrite: true);
        }
        var ini = IniFile.Load(Path);
        ini.Set("General", "Enabled", Enabled ? "1" : "0");
        ini.Set("Connection", "Host", Host);
        ini.Set("Connection", "Port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ini.Set("Misc", "SendIntervalMs", SendIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ini.Set("Misc", "ExtendedData", ExtendedData ? "1" : "0");
        ini.Set("Misc", "ExtendedData1", ExtendedData1 ? "1" : "0");
        ini.Set("Misc", "LogToFile", LogToFile ? "1" : "0");
        ini.Save(Path);
        return backup;
    }

    /// <summary>Recommended settings for Condor2NMEA.</summary>
    public void ApplyRecommended(int port)
    {
        Enabled = true;
        Host = "127.0.0.1";
        Port = port;
        SendIntervalMs = 50; // 20 Hz is plenty for flight computers; 1 ms floods the network stack
        ExtendedData = true;
        ExtendedData1 = true;
    }
}
