using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Core.Traffic;

/// <summary>
/// Keeps the latest Spectate snapshot, identifies the own glider and computes relative positions.
/// </summary>
public sealed class TrafficTracker
{
    private readonly object _lock = new();
    private IReadOnlyList<SpectatePilot> _pilots = Array.Empty<SpectatePilot>();
    private DateTime _sampleTime;
    private string? _autoOwnId;
    private int _autoMatchCount;
    private string? _autoCandidateId;

    /// <summary>Competition number of the own glider. Empty = automatic detection.</summary>
    public string OwnCn { get; set; } = "";
    public bool AutoDetectOwnShip { get; set; } = true;
    /// <summary>Targets further away are not reported. 0 = unlimited.</summary>
    public double MaxRangeMeters { get; set; } = 50_000;
    /// <summary>Extrapolate other gliders with speed/heading between Spectate updates.</summary>
    public bool Extrapolate { get; set; } = true;
    /// <summary>Snapshot older than this is treated as "no traffic".</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromSeconds(10);

    public DateTime SampleTime { get { lock (_lock) return _sampleTime; } }
    public int PilotCount { get { lock (_lock) return _pilots.Count; } }

    public void Update(IReadOnlyList<SpectatePilot> pilots, DateTime sampleTime)
    {
        lock (_lock)
        {
            _pilots = pilots;
            _sampleTime = sampleTime;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pilots = Array.Empty<SpectatePilot>();
            _autoOwnId = null;
        }
    }

    /// <summary>
    /// Determines the own glider. Uses the configured competition number, otherwise the pilot that
    /// matches the live NMEA position for several consecutive updates.
    /// </summary>
    public SpectatePilot? FindOwnShip(GeoPoint? livePosition, double? liveAltitude)
    {
        lock (_lock)
            return FindOwnShipLocked(livePosition, liveAltitude, updateAuto: true);
    }

    private SpectatePilot? FindOwnShipLocked(GeoPoint? livePosition, double? liveAltitude, bool updateAuto)
    {
        if (!string.IsNullOrWhiteSpace(OwnCn))
        {
            return _pilots.FirstOrDefault(p => string.Equals(p.Cn, OwnCn.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? _pilots.FirstOrDefault(p => string.Equals(p.Registration, OwnCn.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (!AutoDetectOwnShip)
            return null;

        // Condor marks the own glider (or, in Spectate! mode, the watched one) with "selected":"True".
        var selected = _pilots.FirstOrDefault(p => p.Selected);
        if (selected is not null)
            return selected;
        if (livePosition is null && _pilots.Count == 1)
            return _pilots[0];

        if (livePosition is { } live && updateAuto)
        {
            SpectatePilot? best = null;
            var bestDist = double.MaxValue;
            foreach (var p in _pilots)
            {
                if (p.Position is not { } pos)
                    continue;
                var d = GeoMath.Distance(live, pos);
                if (liveAltitude is { } la && p.Altitude is { } pa && Math.Abs(la - pa) > 150)
                    continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            // Spectate is updated about once per second; at 250 km/h a glider moves ~70 m.
            if (best is not null && bestDist < 300)
            {
                if (best.Id == _autoCandidateId)
                    _autoMatchCount++;
                else
                {
                    _autoCandidateId = best.Id;
                    _autoMatchCount = 1;
                }
                if (_autoMatchCount >= 3)
                    _autoOwnId = best.Id;
            }
        }
        return _autoOwnId is null ? null : _pilots.FirstOrDefault(p => p.Id == _autoOwnId);
    }

    /// <summary>All pilots of the latest snapshot (for display).</summary>
    public IReadOnlyList<SpectatePilot> Pilots { get { lock (_lock) return _pilots; } }

    /// <summary>
    /// Computes traffic relative to the own ship.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="snapshot">Live flight data (position from NMEA is more current than Spectate).</param>
    public IReadOnlyList<TrafficTarget> GetTargets(DateTime now, FlightSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_pilots.Count == 0 || now - _sampleTime > MaxAge)
                return Array.Empty<TrafficTarget>();

            var liveAlt = snapshot.BaroAltitude ?? snapshot.GpsAltitude;
            var own = FindOwnShipLocked(snapshot.Position, liveAlt, updateAuto: false);
            var dt = Extrapolate ? Math.Clamp((now - _sampleTime).TotalSeconds, 0, 3) : 0;

            // Horizontal reference: live position if available (faster), else own ship from Spectate.
            GeoPoint? reference = snapshot.Position;
            if (reference is null && own?.Position is { } ownPos)
                reference = Move(ownPos, own, dt);
            if (reference is null)
                return Array.Empty<TrafficTarget>();

            // Vertical reference: always the Spectate altitude of the own ship when known, so that
            // both altitudes come from the same source (Condor altimeter). Mixing GPS and Spectate
            // altitudes produced ~40 m offsets in XCSoar.
            double? refAlt = own?.Altitude ?? liveAlt;
            if (own?.Altitude is { } oa && own.Vario is { } ov)
                refAlt = oa + ov * dt;

            var list = new List<TrafficTarget>(_pilots.Count);
            foreach (var p in _pilots)
            {
                if (own is not null && p.Id == own.Id)
                    continue;
                if (p.Position is not { } pos)
                    continue;

                var target = Move(pos, p, dt);
                var (n, e) = GeoMath.RelativeNorthEast(reference.Value, target);
                var alt = (p.Altitude ?? 0) + (p.Vario ?? 0) * dt;
                var v = refAlt is { } ra ? alt - ra : 0;
                var t = new TrafficTarget
                {
                    Pilot = p,
                    HexId = FlarmId(p.Id),
                    RelativeNorth = n,
                    RelativeEast = e,
                    RelativeVertical = v,
                };
                if (MaxRangeMeters > 0 && t.Distance > MaxRangeMeters)
                    continue;
                list.Add(t);
            }
            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return list;
        }
    }

    private static GeoPoint Move(GeoPoint pos, SpectatePilot p, double dt)
    {
        if (dt <= 0 || p.Speed is not { } speed || p.Heading is not { } heading || speed <= 0)
            return pos;
        return GeoMath.Offset(pos, heading, speed * dt);
    }

    /// <summary>Stable 24-bit id: numeric Condor ids are masked, others hashed (FNV-1a).</summary>
    public static uint FlarmId(string id)
    {
        uint n;
        if (ulong.TryParse(id, out var numeric))
        {
            n = (uint)(numeric & 0xFFFFFF);
        }
        else
        {
            var hash = 2166136261u;
            foreach (var ch in id)
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            n = hash & 0xFFFFFF;
        }
        return n == 0 ? 1u : n;
    }
}
