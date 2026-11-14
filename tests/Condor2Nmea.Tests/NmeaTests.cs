using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Tests;

public class NmeaTests
{
    [Fact]
    public void AllSampleSentencesHaveValidChecksums()
    {
        foreach (var line in Samples.Lines("condor3-nmea.txt"))
            Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(line));
    }

    [Fact]
    public void WrongChecksumIsRejected()
    {
        Assert.Null(NmeaLine.TryParse("$GPRMC,122516.707,A,4707.9614,N,01341.9065,E,0.01,251.81,,,,*22"));
        Assert.NotNull(NmeaLine.TryParse("$GPRMC,122516.707,A,4707.9614,N,01341.9065,E,0.01,251.81,,,,*21"));
    }

    [Fact]
    public void ParsesCondor3InFlightSentences()
    {
        var state = new FlightState();
        var parser = new CondorNmeaParser();
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        foreach (var l in new[]
                 {
                     "$GPGGA,131519.518,4639.0074,N,01243.4653,E,1,12,10,2912.3,M,,,,,0000*3D",
                     "$GPRMC,131519.518,A,4639.0074,N,01243.4653,E,165.31,101.12,,,,*25",
                     "$LXWP0,Y,299.4,2912.3,-4.17,,,,,,98,340,19.4*52",
                 })
            Assert.True(parser.Apply(NmeaLine.TryParse(l)!, state, now));

        var s = state.Snapshot(now);
        Assert.Equal(46.650123, s.Position!.Value.Latitude, 5);
        Assert.Equal(12.724422, s.Position!.Value.Longitude, 5);
        Assert.Equal(2912.3, s.GpsAltitude!.Value, 1);
        Assert.Equal(165.31 * 0.514444, s.GroundSpeed!.Value, 3);
        Assert.Equal(101.12, s.Track!.Value, 2);
        Assert.Equal(299.4 / 3.6, s.TrueAirspeed!.Value, 3);
        Assert.Equal(2912.3, s.BaroAltitude!.Value, 1);
        Assert.Equal(-4.17, s.TeVario!.Value, 2);
        Assert.Equal(98, s.Heading!.Value, 1);
        Assert.Equal(340, s.WindDirection!.Value, 1); // Condor 3: direction the wind comes from
        Assert.Equal(19.4 / 3.6, s.WindSpeed!.Value, 3);
        Assert.Equal(new TimeSpan(0, 13, 15, 19, 518), s.TimeOfDay);
        Assert.Null(s.Date); // Condor sends no date
    }

    [Fact]
    public void Condor2WindConventionIsReversed()
    {
        var state = new FlightState();
        var parser = new CondorNmeaParser { WindDirectionIsTo = true };
        var now = DateTime.UtcNow;
        parser.Apply(NmeaLine.TryParse("$LXWP0,Y,299.4,2912.3,-4.17,,,,,,98,340,19.4*52")!, state, now);
        Assert.Equal(160, state.Snapshot(now).WindDirection!.Value, 1);
    }

    [Fact]
    public void GeneratedGpsRoundTrips()
    {
        var state = new FlightState();
        var parser = new CondorNmeaParser();
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        parser.Apply(NmeaLine.TryParse("$GPGGA,131519.518,4639.0074,N,01243.4653,E,1,12,10,2912.3,M,,,,,0000*3D")!, state, now);
        parser.Apply(NmeaLine.TryParse("$GPRMC,131519.518,A,4639.0074,N,01243.4653,E,165.31,101.12,,,,*25")!, state, now);
        var snap = state.Snapshot(now);

        var lines = GpsSentences.Build(snap, now).ToList();
        Assert.Equal(2, lines.Count);
        foreach (var l in lines)
            Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(l));

        var rmc = NmeaLine.TryParse(lines[0])!;
        Assert.Equal("GPRMC", rmc.Type);
        Assert.Equal("131519.52", rmc[0]);
        Assert.Equal("4639.0074", rmc[2]);
        Assert.Equal("01243.4653", rmc[4]);
        Assert.Equal("131126", rmc[8]); // date filled in from the PC clock

        var gga = NmeaLine.TryParse(lines[1])!;
        Assert.Equal("2912.3", gga[8]);
        Assert.Equal("0.0", gga[10]); // explicit geoid separation, altitude stays MSL

        var state2 = new FlightState();
        foreach (var l in lines)
            parser.Apply(NmeaLine.TryParse(l)!, state2, now);
        var s2 = state2.Snapshot(now);
        Assert.Equal(snap.Position!.Value.Latitude, s2.Position!.Value.Latitude, 6);
        Assert.Equal(snap.Position!.Value.Longitude, s2.Position!.Value.Longitude, 6);
        Assert.Equal(snap.GroundSpeed!.Value, s2.GroundSpeed!.Value, 1);
    }

    [Fact]
    public void Lxwp0RoundTrips()
    {
        var state = new FlightState();
        var parser = new CondorNmeaParser();
        var now = DateTime.UtcNow;
        const string original = "$LXWP0,Y,299.4,2912.3,-4.17,,,,,,98,340,19.4*52";
        parser.Apply(NmeaLine.TryParse(original)!, state, now);
        Assert.Equal(original, LxSentences.Lxwp0(state.Snapshot(now)));
        Assert.Contains(",160,19.4*", LxSentences.Lxwp0(state.Snapshot(now), windDirectionTo: true));
    }

    [Theory]
    [InlineData(-0.5, "0030.0000,S")]
    [InlineData(47.13269, "4707.9614,N")]
    [InlineData(47.99999999, "4800.0000,N")]
    public void LatitudeFormatting(double lat, string expected) => Assert.Equal(expected, NmeaFormat.Latitude(lat));
}
