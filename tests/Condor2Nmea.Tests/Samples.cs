namespace Condor2Nmea.Tests;

internal static class Samples
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "samples", name);
    public static string Read(string name) => File.ReadAllText(Path(name));
    public static string[] Lines(string name) => File.ReadAllLines(Path(name)).Where(l => l.Trim().Length > 0).ToArray();

    public static string[] UdpPackets() =>
        System.Text.RegularExpressions.Regex.Split(Read("condor3-udp.txt"), @"\r?\n\s*\r?\n")
            .Where(p => p.Trim().Length > 0).ToArray();
}
