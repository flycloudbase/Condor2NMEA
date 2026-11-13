using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;
using Condor2Nmea.Core.Traffic;

namespace Condor2Nmea.Tests;

public class SpectateTests
{
    [Theory]
    [InlineData("N47.1309", 47.1309)]
    [InlineData("E013.6928", 13.6928)]
    [InlineData("S12.5", -12.5)]
    [InlineData("W001.25", -1.25)]
    [InlineData("45.53.345N", 45 + 53.345 / 60)]   // manual example
    [InlineData("013.53.071E", 13 + 53.071 / 60)]  // manual example
    [InlineData("-33.5", -33.5)]
    [InlineData("47 07.9614 N", 47 + 7.9614 / 60)]
    public void CoordinateFormats(string text, double expected)
    {
        Assert.True(CondorCoordinate.TryParse(text, out var v));
        Assert.Equal(expected, v, 6);
    }

    [Fact]
    public void ParsesRealSpectateFile()
    {
        var pilots = SpectateParser.TryParse(Samples.Read("condor3-spectate.json"))!;
        Assert.Equal(2, pilots.Count);
        var own = pilots[0];
        Assert.Equal("MM", own.Cn);
        Assert.True(own.Selected);
        Assert.Equal(47.1309, own.Position!.Value.Latitude, 6);
        Assert.Equal(13.6928, own.Position!.Value.Longitude, 6);
        Assert.Equal(1106, own.Altitude);
        Assert.Equal(1 / 3.6, own.Speed!.Value, 6);
        Assert.Equal("Max Muster", own.PilotName);
        Assert.False(pilots[1].Selected);
    }

    [Fact]
    public void HalfWrittenFileIsRejected()
    {
        var json = Samples.Read("condor3-spectate.json");
        Assert.Null(SpectateParser.TryParse(json[..(json.Length / 2)]));
        Assert.NotNull(SpectateParser.TryParse("\uFEFF" + json));
    }

    [Fact]
    public void SelectedPilotIsOwnShipAndOthersBecomeFlarmTraffic()
    {
        var pilots = SpectateParser.TryParse(Samples.Read("condor3-spectate.json"))!;
        var now = DateTime.UtcNow;
        var tracker = new TrafficTracker { Extrapolate = false };
        tracker.Update(pilots, now);

        var state = new FlightState();
        var snap = state.Snapshot(now); // no live data: reference = own ship from Spectate
        Assert.Equal("MM", tracker.FindOwnShip(null, null)!.Cn);

        var targets = tracker.GetTargets(now, snap);
        var t = Assert.Single(targets);
        Assert.Equal("AB", t.Pilot.Cn);
        Assert.Equal(0.009 * Math.PI / 180 * GeoMath.EarthRadius, t.RelativeNorth, 0); // ~1002 m north
        Assert.Equal(0, t.RelativeEast, 0);
        Assert.Equal(100, t.RelativeVertical, 3);

        var pflaa = FlarmSentences.PflaaFor(t, new FlarmSentences.Options());
        Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(pflaa));
        var f = NmeaLine.TryParse(pflaa)!;
        Assert.Equal("1002", f[1]);
        Assert.Equal("0", f[2]);
        Assert.Equal("100", f[3]);
        Assert.Equal("00002A!AB", f[5]);
        Assert.Equal("90", f[6]);
        Assert.Equal("40", f[8]);   // 144 km/h = 40 m/s
        Assert.Equal("1.5", f[9]);
        Assert.Equal("1", f[10]);   // glider

        Assert.Equal("$PFLAU,1,1,2,1,0,,0,,,*4D", FlarmSentences.Pflau(1));
        var names = FlarmSentences.Pflam(t).ToList();
        Assert.Contains(names, n => n.Contains("PNAME,4572696B612042656973706965") ); // "Erika Beispie" (17 chars max)
        Assert.All(names, n => Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(n)));
    }

    [Fact]
    public void ExtrapolatesTargetsBetweenUpdates()
    {
        var pilots = SpectateParser.TryParse(Samples.Read("condor3-spectate.json"))!;
        var t0 = DateTime.UtcNow;
        var tracker = new TrafficTracker { Extrapolate = true };
        tracker.Update(pilots, t0);
        var later = tracker.GetTargets(t0.AddSeconds(1), new FlightState().Snapshot(t0.AddSeconds(1)));
        // own ship (1 km/h east) and target (40 m/s east) both move; relative east grows by ~40 m
        Assert.InRange(Assert.Single(later).RelativeEast, 39, 41);
    }

    [Fact]
    public void ConfiguredCompetitionNumberWins()
    {
        var pilots = SpectateParser.TryParse(Samples.Read("condor3-spectate.json"))!;
        var tracker = new TrafficTracker { OwnCn = "ab" };
        tracker.Update(pilots, DateTime.UtcNow);
        Assert.Equal("AB", tracker.FindOwnShip(null, null)!.Cn);
    }

    [Fact]
    public void FlarmIdsAreStable()
    {
        Assert.Equal(0x2Au, TrafficTracker.FlarmId("42"));
        Assert.Equal(1638573153u & 0xFFFFFF, TrafficTracker.FlarmId("1638573153"));
        Assert.Equal(TrafficTracker.FlarmId("abc"), TrafficTracker.FlarmId("abc"));
        Assert.NotEqual(0u, TrafficTracker.FlarmId("0"));
    }
}
