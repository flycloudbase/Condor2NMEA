using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Core.Model;

/// <summary>
/// Thread-safe fusion of all Condor inputs. Every field remembers value, time and source.
/// Priority (while fresh): NMEA &gt; UDP &gt; Spectate.
/// </summary>
public sealed class FlightState
{
    private readonly object _lock = new();

    /// <summary>A higher-priority value blocks lower-priority sources for this long.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>Values older than this are not reported in snapshots.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromSeconds(5);

    internal readonly Field<GeoPoint> Position = new();
    internal readonly Field<double> GpsAltitude = new();
    internal readonly Field<double> GroundSpeed = new();
    internal readonly Field<double> Track = new();
    internal readonly Field<TimeSpan> TimeOfDay = new();
    internal readonly Field<DateOnly> Date = new();
    internal readonly Field<double> BaroAltitude = new();
    internal readonly Field<double> Altimeter = new();
    internal readonly Field<double> TrueAirspeed = new();
    internal readonly Field<double> UdpAirspeed = new();
    internal readonly Field<double> RadioFrequency = new();
    internal readonly Field<double> Heading = new();
    internal readonly Field<double> TeVario = new();
    internal readonly Field<double> PneumaticVario = new();
    internal readonly Field<double> ElectronicVario = new();
    internal readonly Field<double> NettoVario = new();
    internal readonly Field<double> Integrator = new();
    internal readonly Field<double> WindDirection = new();
    internal readonly Field<double> WindSpeed = new();
    internal readonly Field<double> Bank = new();
    internal readonly Field<double> Pitch = new();
    internal readonly Field<double> Yaw = new();
    internal readonly Field<double> RollRate = new();
    internal readonly Field<double> PitchRate = new();
    internal readonly Field<double> YawRate = new();
    internal readonly Field<double> GLoad = new();
    internal readonly Field<double> SlipBall = new();
    internal readonly Field<double> TurnRate = new();
    internal readonly Field<double> YawString = new();
    internal readonly Field<double> HeightAboveGround = new();
    internal readonly Field<double> WheelHeight = new();
    internal readonly Field<double> Turbulence = new();
    internal readonly Field<double> SurfaceRoughness = new();
    internal readonly Field<double> MacCready = new();
    internal readonly Field<double> WaterBallast = new();
    internal readonly Field<int> Flaps = new();
    internal readonly Field<double> GameTime = new();
    internal string? HudMessages;
    internal DateTime HudMessagesTime;
    internal readonly Dictionary<string, double> UdpRaw = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs <paramref name="update"/> under the state lock.</summary>
    public void Update(Action<FlightState> update)
    {
        lock (_lock)
            update(this);
    }

    internal void Set<T>(Field<T> field, T value, DateTime now, DataSource source) where T : struct =>
        field.Update(value, now, source, StaleAfter);

    /// <summary>
    /// Uses the own glider from Spectate.json as low-priority fallback (only takes effect while
    /// no NMEA/UDP values are fresh, e.g. without virtual COM port).
    /// </summary>
    public void ApplySpectateOwnShip(SpectatePilot own, DateTime sampleTime)
    {
        lock (_lock)
        {
            const DataSource src = DataSource.Spectate;
            if (own.Position is { } p) Set(Position, p, sampleTime, src);
            if (own.Altitude is { } a)
            {
                Set(BaroAltitude, a, sampleTime, src);
                Set(GpsAltitude, a, sampleTime, src);
            }
            if (own.Heading is { } h) Set(Track, h, sampleTime, src);
            if (own.Speed is { } v) Set(GroundSpeed, v, sampleTime, src);
            if (own.Vario is { } va) Set(TeVario, va, sampleTime, src);
        }
    }

    public FlightSnapshot Snapshot(DateTime now)
    {
        lock (_lock)
        {
            var age = MaxAge;
            TimeSpan? tod = null;
            var t = TimeOfDay.Get(now, age);
            if (t is { } tv)
                tod = tv + (now - TimeOfDay.Time); // advance the last received time

            return new FlightSnapshot
            {
                Taken = now,
                Position = Position.Get(now, age),
                PositionSource = Position.Get(now, age) is null ? DataSource.None : Position.Source,
                GpsAltitude = GpsAltitude.Get(now, age),
                GroundSpeed = GroundSpeed.Get(now, age),
                Track = Track.Get(now, age),
                TimeOfDay = tod,
                Date = Date.Get(now, TimeSpan.FromHours(1)),
                BaroAltitude = BaroAltitude.Get(now, age),
                Altimeter = Altimeter.Get(now, age),
                TrueAirspeed = TrueAirspeed.Get(now, age),
                Airspeed = UdpAirspeed.Get(now, age),
                RadioFrequency = RadioFrequency.Get(now, TimeSpan.FromSeconds(30)),
                Heading = Heading.Get(now, age),
                TeVario = TeVario.Get(now, age),
                PneumaticVario = PneumaticVario.Get(now, age),
                ElectronicVario = ElectronicVario.Get(now, age),
                NettoVario = NettoVario.Get(now, age),
                Integrator = Integrator.Get(now, age),
                WindDirection = WindDirection.Get(now, age),
                WindSpeed = WindSpeed.Get(now, age),
                Bank = Bank.Get(now, age),
                Pitch = Pitch.Get(now, age),
                Yaw = Yaw.Get(now, age),
                RollRate = RollRate.Get(now, age),
                PitchRate = PitchRate.Get(now, age),
                YawRate = YawRate.Get(now, age),
                GLoad = GLoad.Get(now, age),
                SlipBall = SlipBall.Get(now, age),
                TurnRate = TurnRate.Get(now, age),
                YawString = YawString.Get(now, age),
                HeightAboveGround = HeightAboveGround.Get(now, age),
                WheelHeight = WheelHeight.Get(now, age),
                Turbulence = Turbulence.Get(now, age),
                SurfaceRoughness = SurfaceRoughness.Get(now, age),
                MacCready = MacCready.Get(now, TimeSpan.FromSeconds(30)),
                WaterBallast = WaterBallast.Get(now, TimeSpan.FromSeconds(30)),
                Flaps = Flaps.Get(now, age),
                GameTime = GameTime.Get(now, age),
                HudMessages = now - HudMessagesTime <= age ? HudMessages : null,
                UdpRaw = new Dictionary<string, double>(UdpRaw, StringComparer.OrdinalIgnoreCase),
            };
        }
    }
}
