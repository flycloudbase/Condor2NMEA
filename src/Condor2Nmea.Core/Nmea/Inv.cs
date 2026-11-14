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

    /// <summary>Fixed-point text; never "-0.00" (negative values that round to zero are written as zero).</summary>
    public static string F(double value, int decimals)
    {
        var r = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (r == 0)
            r = 0; // turns -0.0 into +0.0
        return r.ToString("F" + decimals, C);
    }
}
