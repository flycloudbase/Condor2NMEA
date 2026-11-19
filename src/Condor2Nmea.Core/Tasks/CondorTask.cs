using Condor2Nmea.Core.Geo;

namespace Condor2Nmea.Core.Tasks;

/// <summary>Role of a turnpoint in a Condor task.</summary>
public enum TurnpointRole
{
    /// <summary>TP0: departure airfield (not part of the scored task).</summary>
    Takeoff,
    /// <summary>TP1: start line / start sector.</summary>
    Start,
    Turnpoint,
    /// <summary>Last turnpoint: finish.</summary>
    Finish,
}

/// <summary>One turnpoint as stored in a Condor flight plan (.fpl, section [Task]).</summary>
public sealed record CondorTurnpoint
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    /// <summary>Condor landscape coordinates [m]: X grows to the west, Y to the north, origin in the south-east corner.</summary>
    public double X { get; init; }
    public double Y { get; init; }
    /// <summary>Elevation of the turnpoint [m].</summary>
    public double Elevation { get; init; }
    public bool IsAirport { get; init; }
    /// <summary>TPSectorType: 0 = sector/cylinder (radius + angle), 1 = window.</summary>
    public int SectorType { get; init; }
    /// <summary>Sector radius [m].</summary>
    public double Radius { get; init; }
    /// <summary>Sector angle [deg]; 360 = cylinder.</summary>
    public double Angle { get; init; }
    /// <summary>TPAltitude [m] as in Condor (minimum altitude, for windows the window altitude).</summary>
    public double Altitude { get; init; }
    /// <summary>TPWidth [m] (window width).</summary>
    public double Width { get; init; }
    /// <summary>TPHeight [m] (maximum altitude, for windows the window height).</summary>
    public double Height { get; init; }
    /// <summary>TPAzimuth [deg] (window orientation).</summary>
    public double Azimuth { get; init; }
    /// <summary>Filled in by <see cref="CondorTask.WithCoordinates"/>.</summary>
    public GeoPoint? Position { get; init; }
    public TurnpointRole Role { get; init; }
}

/// <summary>A Condor task (flight plan) with turnpoints in landscape and, once converted, WGS84 coordinates.</summary>
public sealed record CondorTask
{
    public string Name { get; init; } = "";
    public string Landscape { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public IReadOnlyList<CondorTurnpoint> Turnpoints { get; init; } = Array.Empty<CondorTurnpoint>();

    public bool HasCoordinates => Turnpoints.Count > 0 && Turnpoints.All(t => t.Position is not null);

    /// <summary>Task distance start → turnpoints → finish [m] (without takeoff), 0 without coordinates.</summary>
    public double Distance
    {
        get
        {
            var pts = Turnpoints.Where(t => t.Role != TurnpointRole.Takeoff && t.Position is not null).Select(t => t.Position!.Value).ToList();
            double d = 0;
            for (var i = 1; i < pts.Count; i++)
                d += GeoMath.Distance(pts[i - 1], pts[i]);
            return d;
        }
    }

    /// <summary>Stable short id of the task content, changes whenever a turnpoint changes.</summary>
    public string Hash
    {
        get
        {
            var text = Landscape + "|" + string.Join("|", Turnpoints.Select(t =>
                FormattableString.Invariant($"{t.Name};{t.X:F1};{t.Y:F1};{t.SectorType};{t.Radius};{t.Angle};{t.Altitude};{t.Width};{t.Height};{t.Azimuth}")));
            var hash = 2166136261u;
            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return hash.ToString("X8");
        }
    }

    public CondorTask WithCoordinates(ICondorCoordinateConverter converter) => this with
    {
        Turnpoints = Turnpoints.Select(t => t with { Position = converter.ToGeo(t.X, t.Y) }).ToList(),
    };
}

/// <summary>Converts Condor landscape coordinates to WGS84 (implemented with Condor's NaviCon.dll on Windows).</summary>
public interface ICondorCoordinateConverter
{
    GeoPoint ToGeo(double x, double y);
}
