using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Condor2Nmea.Runtime.VirtualCom;

/// <summary>One com0com null-modem pair: CNCA&lt;n&gt; ↔ CNCB&lt;n&gt;.</summary>
public sealed record Com0comPair(int Index, string PortA, string PortB, string ParamsA, string ParamsB)
{
    /// <summary>The side Condor writes to (a real "COMx" name).</summary>
    public string CondorPort => IsComName(PortA) ? PortA : IsComName(PortB) ? PortB : PortA;

    /// <summary>The side Condor2NMEA opens.</summary>
    public string AppPort => CondorPort == PortA ? PortB : PortA;

    /// <summary>
    /// Windows has loaded the driver and published the COM port (it appears in HARDWARE\DEVICEMAP\SERIALCOMM).
    /// False means the pair exists in com0com's configuration but the port does not work – usually because
    /// Windows refused the driver (signature).
    /// </summary>
    public bool DriverActive { get; init; } = true;

    private static bool IsComName(string s) => ComPortRegistry.ComNumber(s) is not null;

    public override string ToString() => $"{CondorPort} ↔ {AppPort}";
}

/// <summary>Result of a setupc.exe run.</summary>
public sealed record SetupcResult(int ExitCode, string Output);

/// <summary>
/// Controls com0com (https://sourceforge.net/projects/com0com/) via its command line tool setupc.exe.
/// <list type="bullet">
/// <item>Listing works without administrator rights: the pairs are read from the registry
/// (HKLM\SYSTEM\CurrentControlSet\Services\com0com\Parameters\CNCxN, value PortName).</item>
/// <item>Creating and removing needs administrator rights. setupc.exe is started elevated through a small script
/// in %ProgramData%\Condor2NMEA, which also captures the output and the exit code.</item>
/// <item>com0com 3.0 is only test-signed and does not load on current Windows 10/11; the signed 2.2.2.0 does.
/// Both are detected and reported.</item>
/// </list>
/// </summary>
public sealed partial class Com0comManager
{
    /// <summary>Folder with the signed 2.2.2.0 packages (x64 and i386).</summary>
    public const string DownloadUrl = "https://sourceforge.net/projects/com0com/files/com0com/2.2.2.0/";

    /// <summary>Direct download of the signed 2.2.2.0 package for 64-bit Windows.</summary>
    public const string DirectDownloadUrlX64 =
        "https://sourceforge.net/projects/com0com/files/com0com/2.2.2.0/com0com-2.2.2.0-x64-fre-signed.zip/download";

    /// <summary>
    /// Download for this Windows: the signed x64 package directly; on 32-bit Windows the 2.2.2.0 folder, where the
    /// i386 package has to be picked.
    /// </summary>
    public static string DirectDownloadUrl => Environment.Is64BitOperatingSystem ? DirectDownloadUrlX64 : DownloadUrl;

    /// <summary>Name of the package to install on this Windows.</summary>
    public static string RecommendedPackage =>
        Environment.Is64BitOperatingSystem ? "com0com-2.2.2.0-x64-fre-signed.zip" : "das signierte i386-Paket von com0com 2.2.2.0";

    private const string ParametersKey = @"SYSTEM\CurrentControlSet\Services\com0com\Parameters";
    private static readonly TimeSpan SetupcTimeout = TimeSpan.FromSeconds(120);

    private readonly string? _configuredPath;

    public Com0comManager(string? configuredInstallPath = null) => _configuredPath = configuredInstallPath;

    /// <summary>Folder containing setupc.exe, or null if com0com is not installed.</summary>
    public string? InstallDirectory => FindInstallDirectory(_configuredPath);

    public bool IsInstalled => InstallDirectory is not null;

    /// <summary>File version of the installed com0com (from setupc.exe), null if unknown.</summary>
    public Version? InstalledVersion
    {
        get
        {
            var dir = InstallDirectory;
            if (dir is null)
                return null;
            foreach (var file in new[] { "setupc.exe", "com0com.sys", "setup.dll" })
            {
                try
                {
                    var path = Path.Combine(dir, file);
                    if (!File.Exists(path))
                        continue;
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (info.FileMajorPart > 0)
                        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
                }
                catch (Exception)
                {
                    // try the next file
                }
            }
            return null;
        }
    }

    /// <summary>com0com 3.x is test-signed only and cannot be installed on current Windows 10/11.</summary>
    public static bool IsUnsupportedVersion(Version? v) => v is { Major: >= 3 };

    public static string? FindInstallDirectory(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);
        candidates.AddRange(RegistryCandidates());
        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (!string.IsNullOrEmpty(pf))
                candidates.Add(Path.Combine(pf, "com0com"));
        }
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "setupc.exe")));
    }

    private static IEnumerable<string> RegistryCandidates()
    {
        var result = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(@"SOFTWARE\com0com");
                if (key?.GetValue("Install_Dir") is string dir)
                    result.Add(dir);
                using var uninstall = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\com0com");
                if (uninstall?.GetValue("UninstallString") is string un)
                    result.Add(Path.GetDirectoryName(un.Trim('"'))!);
            }
            catch (Exception)
            {
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ listing

    /// <summary>Lists all pairs from the registry (no administrator rights needed).</summary>
    public IReadOnlyList<Com0comPair> ListPairs() => ReadPairsFromRegistry() ?? Array.Empty<Com0comPair>();

    /// <summary>
    /// Reads HKLM\SYSTEM\CurrentControlSet\Services\com0com\Parameters\CNCxN (value PortName), resolves "COM#"
    /// names and checks with HKLM\HARDWARE\DEVICEMAP\SERIALCOMM whether Windows has published the port.
    /// Returns null if com0com's key does not exist (not installed, or installed without any pair).
    /// </summary>
    public static IReadOnlyList<Com0comPair>? ReadPairsFromRegistry()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var parameters = hklm.OpenSubKey(ParametersKey);
            if (parameters is null)
                return null;

            var serialComm = SerialCommMap(hklm);
            var lines = new List<string>();
            foreach (var id in parameters.GetSubKeyNames())
            {
                var m = ListLine().Match(id);
                if (!m.Success)
                    continue;
                using var key = parameters.OpenSubKey(id);
                var portName = key?.GetValue("PortName") as string ?? id;
                var side = m.Groups[1].Value.Equals("A", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                var line = $"{id.ToUpperInvariant()} PortName={portName}";
                if (serialComm.TryGetValue($@"\Device\com0com{side}{m.Groups[2].Value}", out var real))
                    line += $",RealPortName={real}";
                lines.Add(line);
            }

            var published = new HashSet<string>(serialComm.Values, StringComparer.OrdinalIgnoreCase);
            return ParseList(string.Join("\n", lines))
                .Select(p => p with { DriverActive = published.Contains(p.CondorPort) })
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> SerialCommMap(RegistryKey hklm)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var key = hklm.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
            return map;
        foreach (var name in key.GetValueNames())
            if (key.GetValue(name) is string port)
                map[name] = port;
        return map;
    }

    // ------------------------------------------------------------------ create / remove

    /// <summary>
    /// Creates a new pair: side A gets the name COM&lt;comNumber&gt; (visible to Condor), side B keeps its internal
    /// name CNCB&lt;n&gt; (not shown as a COM port, opened by this program). Waits until Windows has set up the
    /// devices and checks that the port really works.
    /// </summary>
    /// <exception cref="Com0comException">With a user readable German explanation.</exception>
    public async Task<Com0comPair> CreatePairAsync(int comNumber, CancellationToken token = default)
    {
        if (comNumber is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(comNumber));
        var version = InstalledVersion;
        if (IsUnsupportedVersion(version))
            throw new Com0comException(
                $"Installiert ist com0com {version}. Version 3 ist nur testsigniert und wird von Windows 10/11 nicht geladen. " +
                "Bitte com0com über „Apps“ in den Windows-Einstellungen deinstallieren und die signierte Version 2.2.2.0 installieren.");

        var before = ListPairs();
        var result = await RunElevatedAsync($"--silent install PortName=COM{comNumber} -", token).ConfigureAwait(false);

        // setupc prints the new pair ("CNCA1 PortName=COM9" / "CNCB1 PortName=-"); the registry is the reference
        Com0comPair? created = null;
        // Windows installs the devices in the background; give it up to 30 s to publish the port
        for (var attempt = 0; attempt < 60 && !token.IsCancellationRequested; attempt++)
        {
            var now = ListPairs();
            created = now.FirstOrDefault(p => before.All(b => b.Index != p.Index))
                      ?? now.FirstOrDefault(p => string.Equals(p.CondorPort, $"COM{comNumber}", StringComparison.OrdinalIgnoreCase));
            if (created is { DriverActive: true })
                return created;
            await Task.Delay(500, token).ConfigureAwait(false);
        }

        if (created is null)
            throw new Com0comException(
                $"com0com hat kein neues Port-Paar angelegt (Rückgabewert {result.ExitCode}).\n\nAusgabe von setupc:\n{Trim(result.Output)}");

        throw new Com0comException(
            $"Das Port-Paar {created} wurde eingetragen, aber Windows stellt den Port nicht bereit. " +
            "Meist lädt Windows den com0com-Treiber nicht, weil er nicht passend signiert ist (Geräte-Manager: Code 52). " +
            $"Abhilfe: com0com deinstallieren und die signierte Version 2.2.2.0 ({RecommendedPackage}) installieren." +
            (version is null ? "" : $" Gefundene Version: {version}."));
    }

    public async Task RemovePairAsync(int index, CancellationToken token = default)
    {
        var result = await RunElevatedAsync($"--silent remove {index}", token).ConfigureAwait(false);
        if (ListPairs().Any(p => p.Index == index))
            throw new Com0comException(
                $"Das Port-Paar {index} konnte nicht entfernt werden (Rückgabewert {result.ExitCode}).\n\nAusgabe von setupc:\n{Trim(result.Output)}");
    }

    // ------------------------------------------------------------------ parsing

    /// <summary>
    /// Parses "setupc list" output, e.g.
    /// <code>
    ///        CNCA0 PortName=COM9
    ///        CNCB0 PortName=-
    ///        CNCA1 PortName=COM#,RealPortName=COM12,EmuBR=yes
    /// </code>
    /// </summary>
    public static IReadOnlyList<Com0comPair> ParseList(string output)
    {
        var a = new Dictionary<int, (string Name, string Params)>();
        var b = new Dictionary<int, (string Name, string Params)>();
        foreach (var raw in output.Split('\n'))
        {
            var m = ListLine().Match(raw.Trim());
            if (!m.Success)
                continue;
            var side = m.Groups[1].Value.ToUpperInvariant();
            var index = int.Parse(m.Groups[2].Value);
            var prms = m.Groups[3].Value.Trim();
            var id = $"CNC{side}{index}";
            var name = ResolveName(id, prms);
            (side == "A" ? a : b)[index] = (name, prms);
        }
        return a.Keys.Union(b.Keys).OrderBy(i => i)
            .Select(i => new Com0comPair(i,
                a.TryGetValue(i, out var pa) ? pa.Name : $"CNCA{i}",
                b.TryGetValue(i, out var pb) ? pb.Name : $"CNCB{i}",
                a.TryGetValue(i, out pa) ? pa.Params : "",
                b.TryGetValue(i, out pb) ? pb.Params : ""))
            .ToList();
    }

    private static string ResolveName(string id, string prms)
    {
        var values = prms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        if (values.TryGetValue("RealPortName", out var real) && real.Length > 0 && real != "-")
            return real;
        if (values.TryGetValue("PortName", out var name) && name.Length > 0 && name != "-" && name != "COM#")
            return name;
        return id;
    }

    [GeneratedRegex(@"^CNC([AB])(\d+)\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ListLine();

    // ------------------------------------------------------------------ elevated setupc

    /// <summary>
    /// Builds the batch script that runs setupc.exe from its own folder (it needs its .inf files there) and
    /// writes output and exit code to files. A script avoids quoting problems with paths like
    /// "C:\Program Files (x86)\com0com".
    /// </summary>
    public static string BuildScript(string installDir, string arguments, string outputFile, string exitFile) =>
        string.Join("\r\n",
            "@echo off",
            "chcp 65001 >nul",
            $"cd /d \"{installDir}\"",
            $"setupc.exe {arguments} > \"{outputFile}\" 2>&1",
            $"echo %ERRORLEVEL% > \"{exitFile}\"",
            "");

    private async Task<SetupcResult> RunElevatedAsync(string arguments, CancellationToken token)
    {
        var dir = InstallDirectory
                  ?? throw new Com0comException("com0com ist nicht installiert (setupc.exe nicht gefunden).");

        // %ProgramData% is readable for the user and writable for the elevated process, also when UAC asks for
        // another administrator account (the user's %TEMP% would not be accessible then)
        var work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Condor2NMEA", "setupc");
        Directory.CreateDirectory(work);
        var id = Guid.NewGuid().ToString("N");
        var script = Path.Combine(work, $"run-{id}.cmd");
        var output = Path.Combine(work, $"out-{id}.txt");
        var exit = Path.Combine(work, $"exit-{id}.txt");
        await File.WriteAllTextAsync(script, BuildScript(dir, arguments, output, exit), new UTF8Encoding(false), token).ConfigureAwait(false);

        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = dir,
        };
        try
        {
            using var p = Process.Start(psi) ?? throw new Com0comException("setupc.exe konnte nicht gestartet werden.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(SetupcTimeout);
            try
            {
                await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new Com0comException(
                    "setupc.exe antwortet seit 2 Minuten nicht. Vermutlich wartet eine Windows-Abfrage zur Treiberinstallation " +
                    "im Hintergrund – bitte nachsehen und bestätigen, dann erneut versuchen.");
            }

            var text = File.Exists(output) ? await ReadAllTextLenient(output).ConfigureAwait(false) : "";
            var code = File.Exists(exit) && int.TryParse((await File.ReadAllTextAsync(exit, token).ConfigureAwait(false)).Trim(), out var c) ? c : p.ExitCode;
            return new SetupcResult(code, text);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Die Administrator-Abfrage wurde abgebrochen.", ex);
        }
        finally
        {
            foreach (var f in new[] { script, output, exit })
            {
                try { File.Delete(f); } catch (Exception) { }
            }
        }
    }

    private static async Task<string> ReadAllTextLenient(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        // setupc writes in the console code page; UTF-8 after chcp 65001, fall back to Latin-1 for odd characters
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length == 0 ? "(keine Ausgabe)" : s.Length > 1500 ? s[..1500] + " …" : s;
    }
}

/// <summary>A com0com problem with a message that can be shown to the user as it is.</summary>
public sealed class Com0comException(string message) : Exception(message);
