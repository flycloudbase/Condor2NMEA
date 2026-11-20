namespace Condor2Nmea.Core.Traffic;

/// <summary>State of an aircraft for the prediction: position relative to the own glider, ground vector, turn and climb.</summary>
/// <param name="North">Relative position north [m] (own glider: 0).</param>
/// <param name="East">Relative position east [m] (own glider: 0).</param>
/// <param name="Up">Relative altitude [m] (own glider: 0).</param>
/// <param name="GroundSpeed">[m/s]</param>
/// <param name="Track">True track over ground [deg].</param>
/// <param name="TurnRate">[deg/s], positive = turning right.</param>
/// <param name="Climb">[m/s]</param>
public readonly record struct AircraftMotion(double North, double East, double Up, double GroundSpeed, double Track, double TurnRate, double Climb);

/// <param name="AlarmLevel">FLARM alarm level 0…3 (FTD-012: 1 = 15–20 s, 2 = 10–15 s, 3 = 0–10 s to impact).</param>
/// <param name="TimeToImpact">Seconds until the predicted paths enter the protection zone, null if not within the horizon.</param>
/// <param name="ClosestDistance">Smallest predicted horizontal distance within the horizon [m].</param>
public readonly record struct CollisionResult(int AlarmLevel, double? TimeToImpact, double ClosestDistance);

/// <summary>
/// FLARM-like collision prediction. The real FLARM algorithm is patented and not public; what is published
/// (FLARM operating manuals, FTD-012 ICD) is: warnings are based on the time to the predicted collision, not the
/// distance; both flight paths are predicted for roughly the next 20 s from speed, track and turn radius (so circling
/// is handled); there are three levels. This class follows exactly that:
/// <list type="number">
/// <item>Both paths are predicted with a constant turn rate and constant climb ("coordinated turn" model) in steps
/// of <see cref="Step"/> up to <see cref="Horizon"/> seconds.</item>
/// <item>A collision is predicted at the first moment the other aircraft is inside a protection zone around the own
/// glider. The zone grows with the prediction time because the prediction gets less certain:
/// horizontal <see cref="ProtectionRadius"/> + <see cref="RadiusGrowth"/>·t, vertical <see cref="ProtectionHeight"/> +
/// <see cref="HeightGrowth"/>·t.</item>
/// <item>The alarm level follows the time windows of the FLARM data port ICD.</item>
/// </list>
/// Two gliders circling in the same thermal on stable, separated circles are predicted to stay apart and do not warn;
/// converging circles do.
/// </summary>
public static class CollisionPredictor
{
    public const double Horizon = 20;
    public const double Step = 0.25;
    /// <summary>Two wingspans plus GPS error [m].</summary>
    public const double ProtectionRadius = 50;
    public const double RadiusGrowth = 2.0;
    public const double ProtectionHeight = 30;
    public const double HeightGrowth = 1.0;
    /// <summary>Below this ground speed an aircraft counts as standing on the ground [m/s].</summary>
    public const double MinFlyingSpeed = 8;
    public const double MaxTurnRate = 35;

    public static CollisionResult Predict(AircraftMotion own, AircraftMotion other)
    {
        if (own.GroundSpeed < MinFlyingSpeed || other.GroundSpeed < MinFlyingSpeed)
            return new CollisionResult(0, null, Math.Sqrt(other.North * other.North + other.East * other.East));

        var a = new State(own);
        var b = new State(other);
        double closest = double.MaxValue;
        double? impact = null;
        for (var t = 0.0; t <= Horizon + 1e-9; t += Step)
        {
            var dn = b.N - a.N;
            var de = b.E - a.E;
            var dist = Math.Sqrt(dn * dn + de * de);
            closest = Math.Min(closest, dist);
            var dz = Math.Abs(b.U - a.U);
            if (impact is null && dist <= ProtectionRadius + RadiusGrowth * t && dz <= ProtectionHeight + HeightGrowth * t)
                impact = t;
            a.Advance(Step);
            b.Advance(Step);
        }
        return new CollisionResult(Level(impact), impact, closest);
    }

    /// <summary>FTD-012: 3 = 0–10 s, 2 = 10–15 s, 1 = 15–20 s to impact.</summary>
    public static int Level(double? timeToImpact) => timeToImpact switch
    {
        null => 0,
        < 10 => 3,
        < 15 => 2,
        <= Horizon => 1,
        _ => 0,
    };

    private struct State
    {
        public double N, E, U;
        private double _track;
        private readonly double _speed, _turn, _climb;

        public State(AircraftMotion m)
        {
            N = m.North;
            E = m.East;
            U = m.Up;
            _track = m.Track;
            _speed = m.GroundSpeed;
            _turn = Math.Clamp(m.TurnRate, -MaxTurnRate, MaxTurnRate);
            _climb = m.Climb;
        }

        public void Advance(double dt)
        {
            // midpoint integration of the circular path
            var mid = (_track + _turn * dt / 2) * Math.PI / 180;
            N += Math.Cos(mid) * _speed * dt;
            E += Math.Sin(mid) * _speed * dt;
            U += _climb * dt;
            _track += _turn * dt;
        }
    }
}

/// <summary>Estimates a smoothed turn rate from successive track/heading values (one estimator per aircraft).</summary>
public sealed class TurnRateEstimator
{
    private readonly Dictionary<string, (DateTime Time, double Track, double Rate)> _state = new();
    private readonly object _lock = new();
    /// <summary>Weight of a new measurement (exponential smoothing).</summary>
    public double Smoothing { get; set; } = 0.6;

    public double Update(string key, DateTime time, double track)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(key, out var s))
            {
                _state[key] = (time, track, 0);
                return 0;
            }
            var dt = (time - s.Time).TotalSeconds;
            if (dt < 0.3)
                return s.Rate; // same sample again
            if (dt > 5)
            {
                _state[key] = (time, track, 0);
                return 0;
            }
            var d = track - s.Track;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;
            var rate = Math.Clamp(d / dt, -CollisionPredictor.MaxTurnRate, CollisionPredictor.MaxTurnRate);
            var smoothed = s.Rate + Smoothing * (rate - s.Rate);
            _state[key] = (time, track, smoothed);
            return smoothed;
        }
    }

    public double Get(string key)
    {
        lock (_lock)
            return _state.TryGetValue(key, out var s) ? s.Rate : 0;
    }
}

/// <summary>
/// Keeps an alarm for a short time after the prediction no longer sees it, so the warning does not flicker
/// on and off between two updates. Rising levels are passed through at once.
/// </summary>
public sealed class AlarmHold
{
    private readonly Dictionary<uint, (int Level, DateTime Until)> _held = new();
    public TimeSpan HoldTime { get; set; } = TimeSpan.FromSeconds(2);

    public int Apply(uint id, int level, DateTime now)
    {
        if (_held.TryGetValue(id, out var h) && now < h.Until && h.Level > level)
            return h.Level;
        if (level > 0)
            _held[id] = (level, now + HoldTime);
        else
            _held.Remove(id);
        return level;
    }
}
