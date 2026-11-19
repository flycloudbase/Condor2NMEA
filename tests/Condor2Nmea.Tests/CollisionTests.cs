using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;
using Condor2Nmea.Core.Traffic;

namespace Condor2Nmea.Tests;

public class CollisionTests
{
    private static AircraftMotion Own(double speed = 40, double track = 0, double turn = 0) => new(0, 0, 0, speed, track, turn, 0);

    [Theory]
    [InlineData(1600, 1)]   // head-on, 80 m/s closure: (1600 − 50) / 82 ≈ 19 s → level 1
    [InlineData(1100, 2)]   // ~13 s → level 2
    [InlineData(600, 3)]    // ~7 s → level 3
    [InlineData(3000, 0)]   // beyond 20 s
    public void HeadOnLevelsFollowTheIcdTimeWindows(double distance, int expected)
    {
        var other = new AircraftMotion(distance, 0, 0, 40, 180, 0, 0);
        Assert.Equal(expected, CollisionPredictor.Predict(Own(), other).AlarmLevel);
    }

    [Fact]
    public void HeadOnTimeToImpactIsPlausible()
    {
        var r = CollisionPredictor.Predict(Own(), new AircraftMotion(1000, 0, 0, 40, 180, 0, 0));
        // protection zone 50 m + 2 m/s·t: (1000 − 50 − 2t) / 80 = t  →  t ≈ 11.6 s
        Assert.InRange(r.TimeToImpact!.Value, 11, 12);
        Assert.Equal(2, r.AlarmLevel);
    }

    [Fact]
    public void ParallelOrDivergingTrafficDoesNotWarn()
    {
        Assert.Equal(0, CollisionPredictor.Predict(Own(), new AircraftMotion(0, 200, 0, 40, 0, 0, 0)).AlarmLevel);   // side by side
        Assert.Equal(0, CollisionPredictor.Predict(Own(), new AircraftMotion(-300, 0, 0, 40, 180, 0, 0)).AlarmLevel); // flying away
    }

    [Fact]
    public void VerticalSeparationPreventsWarning()
    {
        Assert.Equal(0, CollisionPredictor.Predict(Own(), new AircraftMotion(600, 0, 300, 40, 180, 0, 0)).AlarmLevel);
        Assert.True(CollisionPredictor.Predict(Own(), new AircraftMotion(600, 0, 30, 40, 180, 0, 0)).AlarmLevel > 0);
    }

    [Fact]
    public void SameThermalOnSeparatedCirclesDoesNotWarnButConvergingDoes()
    {
        // both circle right at 20°/s with 30 m/s (radius ≈ 86 m) around the same centre, half a circle apart
        const double speed = 30, rate = 20;
        var radius = speed / (rate * Math.PI / 180);
        // own at west of centre heading north; other at east of centre heading south (same circle, opposite side)
        var own = new AircraftMotion(0, 0, 0, speed, 0, rate, 1.5);
        var other = new AircraftMotion(0, 2 * radius, 0, speed, 180, rate, 1.5);
        Assert.Equal(0, CollisionPredictor.Predict(own, other).AlarmLevel);

        // another glider flies straight to the point where the circling glider will be in 8 s
        const double t = 8;
        var angle = rate * t * Math.PI / 180;                     // turned right by this angle
        var meetNorth = radius * Math.Sin(angle);                 // circle centre is at (0, radius)
        var meetEast = radius - radius * Math.Cos(angle);
        const double intruderTrack = 300;                         // arrives from the south-east
        var dist = 35.0 * t;
        var tr = intruderTrack * Math.PI / 180;
        var intruder = new AircraftMotion(meetNorth - Math.Cos(tr) * dist, meetEast - Math.Sin(tr) * dist, 0, 35, intruderTrack, 0, 0);
        var r = CollisionPredictor.Predict(own, intruder);
        Assert.Equal(3, r.AlarmLevel);
        Assert.InRange(r.TimeToImpact!.Value, 5, 8);
    }

    [Fact]
    public void NoWarningsOnTheGround()
    {
        Assert.Equal(0, CollisionPredictor.Predict(Own(speed: 2), new AircraftMotion(100, 0, 0, 40, 180, 0, 0)).AlarmLevel);
        Assert.Equal(0, CollisionPredictor.Predict(Own(), new AircraftMotion(100, 0, 0, 0, 180, 0, 0)).AlarmLevel);
    }

    [Fact]
    public void TurnRateEstimatorHandlesWrapAround()
    {
        var est = new TurnRateEstimator { Smoothing = 1 };
        var t0 = DateTime.UtcNow;
        est.Update("a", t0, 350);
        Assert.Equal(20, est.Update("a", t0.AddSeconds(1), 10), 3);    // 350 → 10 = +20°/s (right)
        Assert.Equal(-20, est.Update("a", t0.AddSeconds(2), 350), 3);
        Assert.Equal(0, est.Update("a", t0.AddSeconds(20), 0), 3);     // gap → reset
    }

    [Fact]
    public void AlarmIsHeldBrieflyButRisesAtOnce()
    {
        var hold = new AlarmHold();
        var t = DateTime.UtcNow;
        Assert.Equal(2, hold.Apply(1, 2, t));
        Assert.Equal(2, hold.Apply(1, 0, t.AddSeconds(1)));   // held
        Assert.Equal(3, hold.Apply(1, 3, t.AddSeconds(1.5))); // rises at once
        Assert.Equal(0, hold.Apply(1, 0, t.AddSeconds(4)));   // released
    }

    [Fact]
    public void AlarmSentencesFollowTheIcd()
    {
        var target = new TrafficTarget
        {
            Pilot = new SpectatePilot { Id = "42", Cn = "AB", Heading = 180, Speed = 40, Vario = 0 },
            HexId = 0x2A, RelativeNorth = 500, RelativeEast = -500, RelativeVertical = -12,
        };
        var pflaa = NmeaLine.TryParse(FlarmSentences.PflaaFor(target, new FlarmSentences.Options(), 3))!;
        Assert.Equal("3", pflaa[0]);

        var line = FlarmSentences.PflauAlarm(4, FlarmSentences.GpsStatus.Airborne, 3, target, ownTrack: 10);
        Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(line));
        Assert.True(line.Length - 1 <= 80);
        var f = NmeaLine.TryParse(line)!;
        // RX, TX, GPS, Power, AlarmLevel, RelativeBearing, AlarmType, RelativeVertical, RelativeDistance, ID
        Assert.Equal(new[] { "4", "1", "2", "1", "3", "-55", "2", "-12", "707", "00002A" }, f.Fields);
    }
}
