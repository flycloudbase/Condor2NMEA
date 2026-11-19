using System.Globalization;
using System.Text.RegularExpressions;
using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;
using Condor2Nmea.Core.Traffic;

namespace Condor2Nmea.Tests;

/// <summary>Checks the FLARM sentences field by field against FTD-012 Data Port ICD 7.23.</summary>
public class FlarmIcdTests
{
    private static TrafficTarget Target(string id = "1638573153", string cn = "AB", double north = 1234.4, double east = -567.6,
        double vertical = 88.4, double? heading = 359.6, double? speed = 41.2, double? vario = 2.34, string name = "Cédric Günther-Österreicher") =>
        new()
        {
            Pilot = new SpectatePilot
            {
                Id = id, Cn = cn, Registration = "D-KECG", FirstName = name, Plane = "Ventus3-18",
                Heading = heading, Speed = speed, Vario = vario,
            },
            HexId = TrafficTracker.FlarmId(id),
            RelativeNorth = north, RelativeEast = east, RelativeVertical = vertical,
        };

    private static void AssertFrame(string line)
    {
        Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(line));
        // ch. 7: max 80 characters between "$" and CR LF
        Assert.True(line.Length - 1 <= 80, $"too long ({line.Length - 1}): {line}");
        Assert.Matches("^[\\x20-\\x7E]+$", line);
    }

    private static int Int(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Fact]
    public void PflaaFollowsTheIcd()
    {
        var line = FlarmSentences.PflaaFor(Target(), new FlarmSentences.Options());
        AssertFrame(line);
        var f = NmeaLine.TryParse(line)!;
        Assert.Equal("PFLAA", f.Type);
        Assert.Equal(14, f.Fields.Length);                     // data port version 9 layout
        Assert.InRange(Int(f[0]), 0, 3);                       // AlarmLevel
        Assert.Equal(1234, Int(f[1]));                         // RelativeNorth, integer
        Assert.Equal(-568, Int(f[2]));                         // RelativeEast, integer
        Assert.Equal(88, Int(f[3]));                           // RelativeVertical, integer
        Assert.InRange(Int(f[4]), 0, 2);                       // IDType
        Assert.Matches("^[0-9A-F]{6}$", f[5]);                 // ID: 6-digit hex
        Assert.Equal("0", f[6]);                               // Track 0..359 (359.6 rounds to 0)
        Assert.Equal("", f[7]);                                // TurnRate: currently empty
        Assert.Equal("41", f[8]);                              // GroundSpeed integer m/s
        Assert.Equal("2.3", f[9]);                             // ClimbRate one decimal
        Assert.Matches("^[0-9A-F]$", f[10]);                   // AcftType hex
        Assert.Equal("1", f[10]);                              // glider
        Assert.Equal("0", f[11]);                              // NoTrack
        Assert.Equal("0", f[12]);                              // Source FLARM
        Assert.Equal("", f[13]);                               // RSSI unknown
    }

    [Fact]
    public void PflaaRangesAreClamped()
    {
        var f = NmeaLine.TryParse(FlarmSentences.PflaaFor(
            Target(vertical: 40000, vario: -50, speed: 0.2, heading: -10), new FlarmSentences.Options()))!;
        Assert.Equal("32767", f[3]);
        Assert.Equal("-32.7", f[9]);
        Assert.Equal("0", f[8]);      // standing still → 0
        Assert.Equal("350", f[6]);
        var moving = NmeaLine.TryParse(FlarmSentences.PflaaFor(Target(speed: 0.7), new FlarmSentences.Options()))!;
        Assert.Equal("1", moving[8]); // moving → forced > 0
        var unknown = NmeaLine.TryParse(FlarmSentences.PflaaFor(Target(heading: null, speed: null, vario: null), new FlarmSentences.Options()))!;
        Assert.Equal(("", "", ""), (unknown[6], unknown[8], unknown[9]));
    }

    [Fact]
    public void CompetitionNumberInIdOnlyWhenExplicitlyRequested()
    {
        var f = NmeaLine.TryParse(FlarmSentences.PflaaFor(Target(), new FlarmSentences.Options { NonStandardCnInId = true }))!;
        Assert.EndsWith("!AB", f[5]);
    }

    [Theory]
    [InlineData(0, FlarmSentences.GpsStatus.NoFix, "$PFLAU,0,1,0,1,0,,0,,,")]
    [InlineData(3, FlarmSentences.GpsStatus.OnGround, "$PFLAU,3,1,1,1,0,,0,,,")]
    [InlineData(150, FlarmSentences.GpsStatus.Airborne, "$PFLAU,99,1,2,1,0,,0,,,")]
    public void PflauFollowsTheIcd(int count, FlarmSentences.GpsStatus gps, string expected)
    {
        var line = FlarmSentences.Pflau(count, gps);
        AssertFrame(line);
        Assert.StartsWith(expected + "*", line);
        Assert.Equal(10, NmeaLine.TryParse(line)!.Fields.Length); // RX…RelativeDistance, ID empty
    }

    [Fact]
    public void PflamStaysWithinEightyCharactersAndIsUtf8()
    {
        var lines = FlarmSentences.Pflam(Target()).ToList();
        Assert.Equal(4, lines.Count);
        foreach (var l in lines)
        {
            AssertFrame(l);
            var f = NmeaLine.TryParse(l)!;
            Assert.Equal("U", f[0]);
            Assert.Equal("2", f[1]);
            Assert.Matches("^[0-9A-F]{6}$", f[2]);
            Assert.Matches("^([0-9A-F]{2})+$", f[4]);
        }
        var name = NmeaLine.TryParse(lines[0])!;
        Assert.Equal("PNAME", name[3]);
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromHexString(name[4]));
        Assert.StartsWith("Cédric Günther", decoded); // UTF-8 kept, cut at a character boundary
    }

    [Fact]
    public void EngineOrderIsPflaaThenPflau()
    {
        // replay a spectate snapshot with a live position and collect the FLARM block
        var pilots = SpectateParser.TryParse(Samples.Read("condor3-spectate.json"))!;
        var tracker = new TrafficTracker { Extrapolate = false };
        var now = DateTime.UtcNow;
        tracker.Update(pilots, now);
        var targets = tracker.GetTargets(now, new FlightState().Snapshot(now));
        var block = FlarmSentences.Pflaa(targets, new FlarmSentences.Options()).Append(FlarmSentences.Pflau(targets.Count)).ToList();
        Assert.StartsWith("$PFLAA", block[0]);
        Assert.StartsWith("$PFLAU", block[^1]);
        Assert.All(block, AssertFrame);
        Assert.All(block.Where(l => l.StartsWith("$PFLAA")), l => Assert.Matches(new Regex("^[0-9A-F]{6}$"), NmeaLine.TryParse(l)![5]));
    }
}
