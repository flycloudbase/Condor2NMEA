namespace Condor2Nmea.Core.Nmea;

public static class NmeaChecksum
{
    /// <summary>XOR of all characters between '$' (or '!') and '*'.</summary>
    public static byte Compute(ReadOnlySpan<char> body)
    {
        byte cs = 0;
        foreach (var ch in body)
            cs ^= (byte)ch;
        return cs;
    }

    /// <summary>
    /// Builds a complete sentence from a body without leading '$', e.g. "GPRMC,..." → "$GPRMC,...*hh".
    /// </summary>
    public static string Wrap(string body) => $"${body}*{Compute(body):X2}";

    public enum Result { Valid, Missing, Invalid }

    public static Result Verify(ReadOnlySpan<char> line)
    {
        line = line.TrimEnd();
        if (line.Length < 2 || (line[0] != '$' && line[0] != '!'))
            return Result.Invalid;
        var star = line.LastIndexOf('*');
        if (star < 0)
            return Result.Missing;
        if (star + 3 != line.Length)
            return Result.Invalid;
        if (!byte.TryParse(line.Slice(star + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var expected))
            return Result.Invalid;
        return Compute(line.Slice(1, star - 1)) == expected ? Result.Valid : Result.Invalid;
    }
}
