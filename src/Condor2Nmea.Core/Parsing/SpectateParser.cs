using System.Globalization;
using System.Text.Json;
using Condor2Nmea.Core.Geo;

namespace Condor2Nmea.Core.Parsing;

/// <summary>
/// Parses Spectate.json (file written by Condor 3) and the JSON of the Spectate HTTP server
/// (/allPilots, /selectedPilot). Numbers may be encoded as strings, "—"/"-" mean "no value".
/// </summary>
public static class SpectateParser
{
    private const double KmhToMs = 1.0 / 3.6;

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Returns null if the text is not valid JSON (e.g. file is half written).</summary>
    public static IReadOnlyList<SpectatePilot>? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<SpectatePilot>();
        json = json.TrimStart('﻿');
        try
        {
            using var doc = JsonDocument.Parse(json, Options);
            var result = new List<SpectatePilot>();
            Collect(doc.RootElement, result, 0);
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Collect(JsonElement e, List<SpectatePilot> result, int depth)
    {
        if (depth > 3)
            return;
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    Collect(item, result, depth + 1);
                break;
            case JsonValueKind.Object:
                if (LooksLikePilot(e))
                {
                    result.Add(ParsePilot(e));
                }
                else
                {
                    // wrapper object such as { "pilots": [ ... ] }
                    foreach (var p in e.EnumerateObject())
                        if (p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                            Collect(p.Value, result, depth + 1);
                }
                break;
        }
    }

    private static bool LooksLikePilot(JsonElement e) =>
        TryGet(e, "latitude", out _) || TryGet(e, "CN", out _) || TryGet(e, "ID", out _);

    private static SpectatePilot ParsePilot(JsonElement e)
    {
        GeoPoint? pos = null;
        if (CondorCoordinate.TryParse(Str(e, "latitude"), out var lat) &&
            CondorCoordinate.TryParse(Str(e, "longitude"), out var lon))
        {
            var p = new GeoPoint(lat, lon);
            if (p.IsValid && !(lat == 0 && lon == 0))
                pos = p;
        }

        return new SpectatePilot
        {
            Id = Str(e, "ID"),
            Cn = Str(e, "CN"),
            Registration = Str(e, "RN"),
            FirstName = Str(e, "firstname"),
            LastName = Str(e, "lastname"),
            Country = Str(e, "country"),
            Plane = Str(e, "plane"),
            Position = pos,
            Altitude = Num(e, "altitude"),
            Speed = Num(e, "speed") is { } kmh ? kmh * KmhToMs : null,
            Heading = Num(e, "heading") is { } h ? GeoMath.NormalizeDegrees(h) : null,
            Vario = Num(e, "vario"),
            PlayerStatus = Str(e, "playerstatus"),
            Rank = Str(e, "rank"),
            Score = Str(e, "score"),
            Distance = Str(e, "dist"),
            AverageSpeed = Str(e, "averagespeed"),
            Selected = Str(e, "selected").Equals("true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string Str(JsonElement e, string name)
    {
        if (!TryGet(e, name, out var v))
            return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => (v.GetString() ?? "").Trim(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static double? Num(JsonElement e, string name)
    {
        if (!TryGet(e, name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number)
            return v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
        if (v.ValueKind != JsonValueKind.String)
            return null;
        var s = (v.GetString() ?? "").Trim();
        // strip unit suffixes like "70 km/h" or "0.0 p"
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] is '.' or '-' or '+' or 'e' or 'E'))
            end++;
        if (end == 0)
            return null;
        return double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && double.IsFinite(r)
            ? r
            : null;
    }
}
