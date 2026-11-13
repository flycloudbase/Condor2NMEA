using System.Diagnostics;

namespace Condor2Nmea.Runtime.CondorFiles;

public static class CondorInstallation
{
    public static readonly string[] CandidatePaths =
    {
        @"C:\Condor3",
        @"C:\Program Files\Condor3",
        @"C:\Program Files (x86)\Condor3",
        @"D:\Condor3",
    };

    public static string DefaultSpectatePath(string installPath) =>
        Path.Combine(string.IsNullOrWhiteSpace(installPath) ? @"C:\Condor3" : installPath, "Logs", "Spectate.json");

    /// <summary>Returns the first folder that looks like a Condor 3 installation.</summary>
    public static string? Detect()
    {
        foreach (var p in CandidatePaths)
        {
            try
            {
                if (Directory.Exists(p) && (Directory.Exists(Path.Combine(p, "Settings")) ||
                                            File.Exists(Path.Combine(p, "Condor.exe"))))
                    return p;
            }
            catch (Exception)
            {
                // ignore inaccessible drives
            }
        }
        return null;
    }

    /// <summary>True if a Condor process is running.</summary>
    public static bool IsCondorRunning()
    {
        foreach (var name in new[] { "Condor", "Condor3", "CondorClient" })
        {
            var procs = Process.GetProcessesByName(name);
            var running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (running)
                return true;
        }
        return false;
    }
}
