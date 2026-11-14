using System.Globalization;

namespace Condor2Nmea.Core.Nmea;

public static class NmeaFormat
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>"ddmm.mmmm,N"</summary>
    public static string Latitude(double latitude)
    {
        var hemi = latitude < 0 ? 'S' : 'N';
        var (deg, min) = Split(Math.Abs(latitude), 4);
        return string.Create(C, $"{deg:00}{min:00.0000},{hemi}");
    }

    /// <summary>"dddmm.mmmm,E"</summary>
    public static string Longitude(double longitude)
    {
        var hemi = longitude < 0 ? 'W' : 'E';
        var (deg, min) = Split(Math.Abs(longitude), 4);
        return string.Create(C, $"{deg:000}{min:00.0000},{hemi}");
    }

    private static (int Degrees, double Minutes) Split(double value, int minuteDecimals)
    {
        var deg = (int)Math.Floor(value);
        var min = Math.Round((value - deg) * 60.0, minuteDecimals);
        if (min >= 60.0)
        {
            deg += 1;
            min = 0;
        }
        return (deg, min);
    }

    /// <summary>"hhmmss.ss"</summary>
    public static string Time(TimeSpan timeOfDay)
    {
        var t = TimeSpan.FromTicks(((timeOfDay.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay);
        var seconds = t.Seconds + t.Milliseconds / 1000.0;
        return string.Create(C, $"{t.Hours:00}{t.Minutes:00}{seconds:00.00}");
    }

    /// <summary>"ddmmyy"</summary>
    public static string Date(DateOnly date) => date.ToString("ddMMyy", C);

    /// <summary>Parses "hhmmss[.ss]".</summary>
    public static TimeSpan? ParseTime(string field)
    {
        if (field.Length < 6)
            return null;
        if (!int.TryParse(field.AsSpan(0, 2), NumberStyles.None, C, out var h) ||
            !int.TryParse(field.AsSpan(2, 2), NumberStyles.None, C, out var m) ||
            !double.TryParse(field.AsSpan(4), NumberStyles.Float, C, out var s))
            return null;
        if (h > 23 || m > 59 || s >= 61)
            return null;
        return new TimeSpan(h, m, 0) + TimeSpan.FromSeconds(s);
    }

    /// <summary>Parses "ddmmyy".</summary>
    public static DateOnly? ParseDate(string field)
    {
        if (field.Length != 6)
            return null;
        return DateOnly.TryParseExact(field, "ddMMyy", C, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Parses "ddmm.mmmm" + hemisphere into signed decimal degrees.</summary>
    public static double? ParseCoordinate(string value, string hemisphere)
    {
        if (!Inv.TryDouble(value, out var raw) || hemisphere.Length != 1)
            return null;
        var deg = Math.Floor(raw / 100.0);
        var min = raw - deg * 100.0;
        if (min >= 60)
            return null;
        var result = deg + min / 60.0;
        return hemisphere[0] switch
        {
            'N' or 'E' => result,
            'S' or 'W' => -result,
            _ => null,
        };
    }
}
