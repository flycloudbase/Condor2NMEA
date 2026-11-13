using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
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

    private static bool IsComName(string s) => ComPortRegistry.ComNumber(s) is not null;

    public override string ToString() => $"{CondorPort} ↔ {AppPort}";
}

/// <summary>
/// Controls com0com (https://com0com.sourceforge.net) via its command line tool setupc.exe.
/// Creating and removing ports requires administrator rights; the tool is then started elevated
/// (UAC prompt) and its output is captured through a temporary file.
/// </summary>
public sealed partial class Com0comManager
{
    public const string DownloadUrl = "https://sourceforge.net/projects/com0com/files/com0com/";

    private readonly string? _configuredPath;

    public Com0comManager(string? configuredInstallPath = null) => _configuredPath = configuredInstallPath;

    /// <summary>Folder containing setupc.exe, or null if com0com is not installed.</summary>
    public string? InstallDirectory => FindInstallDirectory(_configuredPath);

    public bool IsInstalled => InstallDirectory is not null;

    public static string? FindInstallDirectory(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);
        if (OperatingSystem.IsWindows())
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

    [SupportedOSPlatform("windows")]
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

    /// <summary>Lists all pairs ("setupc list"; does not need admin rights).</summary>
    public async Task<IReadOnlyList<Com0comPair>> ListPairsAsync()
    {
        var output = await RunAsync("list", elevated: false).ConfigureAwait(false);
        return ParseList(output);
    }

    /// <summary>
    /// Creates a new pair: side A gets the name COM&lt;comNumber&gt; (visible to Condor), side B keeps
    /// its internal name CNCB&lt;n&gt; (not shown as a COM port, opened by this program).
    /// </summary>
    public async Task<Com0comPair> CreatePairAsync(int comNumber)
    {
        if (comNumber is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(comNumber));
        var before = await ListPairsAsync().ConfigureAwait(false);
        var output = await RunAsync($"--silent install PortName=COM{comNumber} -", elevated: true).ConfigureAwait(false);
        var after = await ListPairsAsync().ConfigureAwait(false);
        var created = after.FirstOrDefault(p => before.All(b => b.Index != p.Index));
        return created ?? throw new InvalidOperationException("com0com hat kein neues Paar angelegt:\n" + output);
    }

    public Task RemovePairAsync(int index) => RunAsync($"--silent remove {index}", elevated: true);

    /// <summary>Renames one side, e.g. ("CNCA0", "COM9").</summary>
    public Task RenamePortAsync(string portId, string newName) =>
        RunAsync($"--silent change {portId} PortName={newName}", elevated: true);

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

    // ------------------------------------------------------------------ process

    private async Task<string> RunAsync(string arguments, bool elevated)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("com0com gibt es nur unter Windows.");
        var dir = InstallDirectory
                  ?? throw new InvalidOperationException("com0com ist nicht installiert (setupc.exe nicht gefunden).");
        var setupc = Path.Combine(dir, "setupc.exe");

        if (!elevated)
        {
            var psi = new ProcessStartInfo(setupc, arguments)
            {
                WorkingDirectory = dir, // setupc needs its .inf files from the working directory
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("setupc.exe startet nicht");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync().ConfigureAwait(false);
            return await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        }

        // Elevated: ShellExecute "runas" cannot redirect, so let cmd write the output to a temp file.
        var tmp = Path.Combine(Path.GetTempPath(), $"condor2nmea-setupc-{Guid.NewGuid():N}.txt");
        var cmd = $"/s /c \"cd /d \"{dir}\" && setupc.exe {arguments} > \"{tmp}\" 2>&1\"";
        var elevatedPsi = new ProcessStartInfo("cmd.exe", cmd)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(elevatedPsi) ?? throw new InvalidOperationException("setupc.exe startet nicht");
            await p.WaitForExitAsync().ConfigureAwait(false);
            var output = File.Exists(tmp) ? await File.ReadAllTextAsync(tmp).ConfigureAwait(false) : "";
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"setupc.exe {arguments} fehlgeschlagen (Code {p.ExitCode}):\n{output}");
            return output;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Administratorrechte wurden abgelehnt.", ex);
        }
        finally
        {
            try { File.Delete(tmp); } catch (Exception) { }
        }
    }
}
