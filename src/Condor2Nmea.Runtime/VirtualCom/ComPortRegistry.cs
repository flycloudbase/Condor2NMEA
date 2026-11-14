using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Condor2Nmea.Runtime.VirtualCom;

/// <summary>Reads which COM port numbers exist or are reserved on this PC.</summary>
public static partial class ComPortRegistry
{
    /// <summary>Port names currently present (HKLM\HARDWARE\DEVICEMAP\SERIALCOMM).</summary>
    public static IReadOnlyList<string> GetPresentPorts()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();
        return GetPresentPortsWindows();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GetPresentPortsWindows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
            return Array.Empty<string>();
        return key.GetValueNames()
            .Select(n => key.GetValue(n) as string)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .OrderBy(ComNumberOrMax)
            .ToList();
    }

    /// <summary>
    /// COM numbers that are in use or reserved (present ports + "COM Name Arbiter" ComDB bitmap).
    /// </summary>
    public static ISet<int> GetUsedComNumbers()
    {
        var used = new HashSet<int>();
        foreach (var p in GetPresentPorts())
            if (ComNumber(p) is { } n)
                used.Add(n);
        if (OperatingSystem.IsWindows())
            AddComDb(used);
        return used;
    }

    [SupportedOSPlatform("windows")]
    private static void AddComDb(HashSet<int> used)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter");
            if (key?.GetValue("ComDB") is not byte[] db)
                return;
            for (var i = 0; i < db.Length * 8; i++)
                if ((db[i / 8] & (1 << (i % 8))) != 0)
                    used.Add(i + 1);
        }
        catch (Exception)
        {
            // not readable – present ports are enough
        }
    }

    /// <summary>
    /// The Condor manual recommends a number above the highest existing port. We pick the lowest
    /// free number above the highest present port, at least COM5.
    /// </summary>
    public static int SuggestFreeComNumber()
    {
        var present = GetPresentPorts().Select(ComNumber).Where(n => n is not null).Select(n => n!.Value).ToList();
        var used = GetUsedComNumbers();
        var candidate = Math.Max(5, present.Count == 0 ? 5 : present.Max() + 1);
        while (used.Contains(candidate) && candidate < 255)
            candidate++;
        return candidate;
    }

    public static int? ComNumber(string portName)
    {
        var m = ComRegex().Match(portName.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private static int ComNumberOrMax(string name) => ComNumber(name) ?? int.MaxValue;

    [GeneratedRegex(@"^COM(\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex ComRegex();
}
