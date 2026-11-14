using System.Globalization;

namespace Condor2Nmea.Core.Nmea;

/// <summary>Invariant-culture number helpers (NMEA and Condor always use '.' as decimal separator).</summary>
internal static class Inv
{
    public static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static bool TryDouble(string? s, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        return double.TryParse(s.Trim(), NumberStyles.Float, C, out value) && double.IsFinite(value);
    }

    public static double? Double(string? s) => TryDouble(s, out var v) ? v : null;

    public static string F(double value, int decimals) => value.ToString("F" + decimals, C);
}
