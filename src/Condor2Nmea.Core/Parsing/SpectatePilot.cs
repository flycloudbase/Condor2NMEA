using Condor2Nmea.Core.Geo;

namespace Condor2Nmea.Core.Parsing;

/// <summary>One pilot entry of Spectate.json / the Spectate HTTP API.</summary>
public sealed record SpectatePilot
{
    public string Id { get; init; } = "";
    public string Cn { get; init; } = "";
    public string Registration { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Country { get; init; } = "";
    public string Plane { get; init; } = "";
    public GeoPoint? Position { get; init; }
    /// <summary>Altitude MSL as shown by Condor's altimeter [m].</summary>
    public double? Altitude { get; init; }
    /// <summary>Speed [m/s] (Spectate reports km/h).</summary>
    public double? Speed { get; init; }
    public double? Heading { get; init; }
    /// <summary>Vertical speed [m/s].</summary>
    public double? Vario { get; init; }
    public string PlayerStatus { get; init; } = "";
    public string Rank { get; init; } = "";
    public string Score { get; init; } = "";
    public string Distance { get; init; } = "";
    public string AverageSpeed { get; init; } = "";
    /// <summary>"selected" – the pilot currently selected in Condor (true for the own glider when flying).</summary>
    public bool Selected { get; init; }

    public string PilotName => $"{FirstName} {LastName}".Trim();
    public string DisplayName => Cn.Length > 0 ? Cn : Registration.Length > 0 ? Registration : Id;
}
