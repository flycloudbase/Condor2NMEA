using System.Globalization;

namespace Condor2Nmea.Core.Tasks;

/// <summary>
/// Reads the [Task] section of a Condor flight plan (.fpl). Keys per turnpoint i: TPName{i}, TPPosX{i}, TPPosY{i},
/// TPPosZ{i}, TPAirport{i}, TPSectorType{i}, TPRadius{i}, TPAngle{i}, TPAltitude{i}, TPWidth{i}, TPHeight{i}, TPAzimuth{i}.
/// TP0 is the departure airfield, TP1 the start, the last turnpoint the finish.
/// </summary>
public static class FplParser
{
    public static CondorTask Parse(string text, string sourceFile = "")
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inTask = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inTask = line.Equals("[Task]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inTask || line.Length == 0 || line[0] is ';' or '#')
                continue;
            var eq = line.IndexOf('=');
            if (eq > 0)
                values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        if (!values.TryGetValue("Count", out var countText) || !int.TryParse(countText, out var count) || count <= 0)
            throw new FormatException("Die Datei enthält keine Aufgabe ([Task] mit Count fehlt).");

        string S(string key, int i) => values.TryGetValue(key + i.ToString(CultureInfo.InvariantCulture), out var v) ? v : "";
        double D(string key, int i) =>
            double.TryParse(S(key, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        var tps = new List<CondorTurnpoint>(count);
        for (var i = 0; i < count; i++)
        {
            if (S("TPPosX", i).Length == 0 || S("TPPosY", i).Length == 0)
                throw new FormatException($"Wendepunkt {i} hat keine Koordinaten.");
            var role = i == 0 ? TurnpointRole.Takeoff
                : i == 1 ? TurnpointRole.Start
                : i == count - 1 ? TurnpointRole.Finish
                : TurnpointRole.Turnpoint;
            tps.Add(new CondorTurnpoint
            {
                Index = i,
                Name = S("TPName", i),
                X = D("TPPosX", i),
                Y = D("TPPosY", i),
                Elevation = D("TPPosZ", i),
                IsAirport = S("TPAirport", i) == "1",
                SectorType = (int)D("TPSectorType", i),
                Radius = D("TPRadius", i),
                Angle = D("TPAngle", i),
                Altitude = D("TPAltitude", i),
                Width = D("TPWidth", i),
                Height = D("TPHeight", i),
                Azimuth = D("TPAzimuth", i),
                Role = role,
            });
        }

        return new CondorTask
        {
            Name = values.TryGetValue("TaskName", out var n) ? n : Path.GetFileNameWithoutExtension(sourceFile),
            Landscape = values.TryGetValue("Landscape", out var l) ? l : "",
            SourceFile = sourceFile,
            Turnpoints = tps,
        };
    }
}
