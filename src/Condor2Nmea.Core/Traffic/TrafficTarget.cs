using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Core.Traffic;

/// <summary>Another glider relative to the own ship.</summary>
public sealed record TrafficTarget
{
    public required SpectatePilot Pilot { get; init; }
    /// <summary>24-bit FLARM-like id derived from the Condor id.</summary>
    public required uint HexId { get; init; }
    public double RelativeNorth { get; init; }
    public double RelativeEast { get; init; }
    public double RelativeVertical { get; init; }
    public double Distance => Math.Sqrt(RelativeNorth * RelativeNorth + RelativeEast * RelativeEast);
    /// <summary>Bearing from own ship [deg true].</summary>
    public double Bearing => (Math.Atan2(RelativeEast, RelativeNorth) * 180.0 / Math.PI + 360.0) % 360.0;
}
