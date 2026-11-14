using System.Globalization;

namespace Condor2Nmea.Core.Parsing;

/// <summary>
/// Parses the coordinate strings of Spectate.json. Several notations are accepted because the
/// manual and real files differ:
/// <list type="bullet">
/// <item>"45.53.345N" / "013.53.071E" – degrees.minutes.thousandths of a minute (manual example)</item>
/// <item>"N45.890" / "45.890N" – decimal degrees with hemisphere letter (XCSoar's interpretation)</item>
/// <item>"-45.890" – signed decimal degrees</item>
/// <item>"45 53.345 N", "45°53.345'N" – degrees + decimal minutes</item>
/// </list>
/// </summary>
public static class CondorCoordinate
{
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().ToUpperInvariant()
            .Replace('°', ' ').Replace('\'', ' ').Replace('"', ' ').Replace(',', '.');

        var sign = 1;
        char? hemi = null;
        if (s.Length > 0 && "NSEW".Contains(s[0])) { hemi = s[0]; s = s[1..]; }
        else if (s.Length > 0 && "NSEW".Contains(s[^1])) { hemi = s[^1]; s = s[..^1]; }
        if (hemi is 'S' or 'W') sign = -1;

        s = s.Trim();
        if (s.StartsWith('-')) { sign = -sign; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];

        double result;
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            // "45 53.345"
            if (!TryD(parts[0], out var d) || !TryD(parts[1], out var m) || m >= 60)
                return false;
            result = d + m / 60.0;
        }
        else if (parts.Length == 1)
        {
            var dots = s.Split('.');
            if (dots.Length == 3)
            {
                // "45.53.345" = 45° 53.345'
                if (!int.TryParse(dots[0], NumberStyles.None, CultureInfo.InvariantCulture, out var d) ||
                    !TryD(dots[1] + "." + dots[2], out var m) || m >= 60)
                    return false;
                result = d + m / 60.0;
            }
            else if (!TryD(s, out result))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        value = sign * result;
        return double.IsFinite(value);
    }

    private static bool TryD(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
