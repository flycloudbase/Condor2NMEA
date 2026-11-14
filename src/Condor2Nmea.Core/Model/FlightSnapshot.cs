using Condor2Nmea.Core.Geo;

namespace Condor2Nmea.Core.Model;

/// <summary>
/// Immutable view of all values that are currently fresh. All values are in SI units unless
/// stated otherwise; angles in degrees.
/// </summary>
public sealed record FlightSnapshot
{
    public DateTime Taken { get; init; }

    public GeoPoint? Position { get; init; }
    public DataSource PositionSource { get; init; }
    /// <summary>GPS altitude above MSL [m].</summary>
    public double? GpsAltitude { get; init; }
    /// <summary>Ground speed [m/s].</summary>
    public double? GroundSpeed { get; init; }
    /// <summary>Track over ground [deg true].</summary>
    public double? Track { get; init; }
    /// <summary>Time of day of the simulation, already advanced to <see cref="Taken"/>.</summary>
    public TimeSpan? TimeOfDay { get; init; }
    public DateOnly? Date { get; init; }

    /// <summary>Barometric altitude MSL as Condor reports it in LXWP0 [m].</summary>
    public double? BaroAltitude { get; init; }
    /// <summary>Altimeter instrument reading (QNH/QFE, m or ft depending on Condor settings).</summary>
    public double? Altimeter { get; init; }
    /// <summary>True airspeed [m/s].</summary>
    public double? TrueAirspeed { get; init; }
    /// <summary>
    /// "airspeed" from UDP [m/s] (not in manual 1.03; probably IAS). Only used as TAS fallback.
    /// </summary>
    public double? Airspeed { get; init; }
    /// <summary>Active COM frequency [MHz] (UDP "radiofrequency", not in manual 1.03).</summary>
    public double? RadioFrequency { get; init; }
    /// <summary>Heading of the glider [deg].</summary>
    public double? Heading { get; init; }

    /// <summary>Total energy vario [m/s].</summary>
    public double? TeVario { get; init; }
    public double? PneumaticVario { get; init; }
    public double? ElectronicVario { get; init; }
    public double? NettoVario { get; init; }
    public double? Integrator { get; init; }

    /// <summary>Wind direction (where the wind comes FROM) [deg].</summary>
    public double? WindDirection { get; init; }
    /// <summary>Wind speed [m/s].</summary>
    public double? WindSpeed { get; init; }

    /// <summary>Bank angle [deg], aviation convention: positive = right wing down.</summary>
    public double? Bank { get; init; }
    /// <summary>Pitch angle [deg], positive = nose up.</summary>
    public double? Pitch { get; init; }
    public double? Yaw { get; init; }
    public double? RollRate { get; init; }
    public double? PitchRate { get; init; }
    public double? YawRate { get; init; }
    public double? GLoad { get; init; }
    public double? SlipBall { get; init; }
    public double? TurnRate { get; init; }
    public double? YawString { get; init; }

    public double? HeightAboveGround { get; init; }
    public double? WheelHeight { get; init; }
    public double? Turbulence { get; init; }
    public double? SurfaceRoughness { get; init; }

    public double? MacCready { get; init; }
    /// <summary>Water ballast [kg].</summary>
    public double? WaterBallast { get; init; }
    public int? Flaps { get; init; }
    /// <summary>In-game display time [decimal hours].</summary>
    public double? GameTime { get; init; }
    public string? HudMessages { get; init; }

    /// <summary>All numeric UDP parameters as received (lower-case keys).</summary>
    public IReadOnlyDictionary<string, double> UdpRaw { get; init; } = new Dictionary<string, double>();
}
