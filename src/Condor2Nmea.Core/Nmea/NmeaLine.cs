namespace Condor2Nmea.Core.Nmea;

/// <summary>A tokenised NMEA sentence ("$GPRMC,a,b*hh" → Type="GPRMC", Fields=[a,b]).</summary>
public sealed class NmeaLine
{
    public string Raw { get; }
    public string Type { get; }
    public string[] Fields { get; }
    public NmeaChecksum.Result Checksum { get; }

    private NmeaLine(string raw, string type, string[] fields, NmeaChecksum.Result checksum)
    {
        Raw = raw;
        Type = type;
        Fields = fields;
        Checksum = checksum;
    }

    public string this[int index] => index < Fields.Length ? Fields[index] : string.Empty;

    /// <summary>
    /// Parses a line. Lines with a wrong checksum are rejected; lines without checksum are accepted.
    /// </summary>
    public static NmeaLine? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        line = line.Trim();
        if (line[0] != '$' && line[0] != '!')
            return null;

        var cs = NmeaChecksum.Verify(line);
        if (cs == NmeaChecksum.Result.Invalid)
            return null;

        var end = line.LastIndexOf('*');
        var body = end >= 0 ? line[1..end] : line[1..];
        var parts = body.Split(',');
        if (parts.Length == 0 || parts[0].Length == 0)
            return null;
        return new NmeaLine(line, parts[0], parts[1..], cs);
    }

    public double? Double(int index) => Inv.Double(this[index]);
}
