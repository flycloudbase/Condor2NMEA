using Condor2Nmea.Core.Config;
using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Tasks;
using Condor2Nmea.Runtime.Tasks;

namespace Condor2Nmea.Tests;

public class TaskTests
{
    /// <summary>Linear stand-in for NaviCon.dll: X grows to the west, Y to the north.</summary>
    private sealed class FakeConverter : ICondorCoordinateConverter
    {
        public GeoPoint ToGeo(double x, double y) => new(46.0 + y / 111_000.0, 12.0 - x / 77_000.0);
    }

    [Fact]
    public void ParsesCondorFlightPlan()
    {
        var task = FplParser.Parse(Samples.Read("condor-task.fpl"), "condor-task.fpl");
        Assert.Equal("Testaufgabe Muster", task.Name);
        Assert.Equal("TestLandscape", task.Landscape);
        Assert.Equal(5, task.Turnpoints.Count);
        Assert.Equal(new[] { TurnpointRole.Takeoff, TurnpointRole.Start, TurnpointRole.Turnpoint, TurnpointRole.Turnpoint, TurnpointRole.Finish },
            task.Turnpoints.Select(t => t.Role));
        var window = task.Turnpoints[3];
        Assert.Equal(1, window.SectorType);
        Assert.Equal(1000, window.Width);
        Assert.Equal(45, window.Azimuth);
        Assert.Equal(100000.5, task.Turnpoints[0].X);
        Assert.True(task.Turnpoints[0].IsAirport);
        Assert.False(task.HasCoordinates);
    }

    [Fact]
    public void BrokenFlightPlanIsRejected()
    {
        Assert.Throws<FormatException>(() => FplParser.Parse("[Task]\nTaskName=x\n"));
        Assert.Throws<FormatException>(() => FplParser.Parse("[Task]\nCount=1\nTPName0=a\n"));
    }

    [Fact]
    public void TaskSentencesCarryEveryTurnpoint()
    {
        var task = FplParser.Parse(Samples.Read("condor-task.fpl")).WithCoordinates(new FakeConverter());
        var lines = TaskSentences.Build(task);
        Assert.Equal(6, lines.Count);
        foreach (var l in lines)
        {
            Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(l));
            Assert.True(l.Length <= 140, l);
            Assert.Matches("^[\\x20-\\x7E]+$", l);
        }

        var header = NmeaLine.TryParse(lines[0])!;
        Assert.Equal("PCBC", header.Type);
        Assert.Equal(new[] { "TASK", "1", task.Hash, "5" }, header.Fields.Take(4));
        Assert.Equal("TestLandscape", header[5]);
        Assert.Equal("Testaufgabe Muster", header[6]);

        var tp = NmeaLine.TryParse(lines[3])!; // TP2 "Gipfel, Süd"
        Assert.Equal("TP", tp[0]);
        Assert.Equal(task.Hash, tp[2]);
        Assert.Equal(("2", "5", "W"), (tp[3], tp[4], tp[5]));
        Assert.Equal(46.0 + 60000 / 111000.0, double.Parse(tp[6], System.Globalization.CultureInfo.InvariantCulture), 5);
        Assert.Equal(12.0 - 80000 / 77000.0, double.Parse(tp[7], System.Globalization.CultureInfo.InvariantCulture), 5);
        Assert.Equal(("2100", "0", "500", "360"), (tp[8], tp[9], tp[10], tp[11]));
        Assert.Equal("Gipfel Sud", tp[17]); // comma removed, ASCII

        var start = NmeaLine.TryParse(lines[2])!;
        Assert.Equal("S", start[5]);
        Assert.Equal("90", start[11]);
        var finish = NmeaLine.TryParse(lines[5])!;
        Assert.Equal(("F", "1"), (finish[5], finish[16]));
        var windowTp = NmeaLine.TryParse(lines[4])!;
        Assert.Equal(("1", "1800", "1000", "400", "45"), (windowTp[9], windowTp[12], windowTp[13], windowTp[14], windowTp[15]));
    }

    [Fact]
    public void HashChangesWithTheTask()
    {
        var a = FplParser.Parse(Samples.Read("condor-task.fpl"));
        var b = a with { Turnpoints = a.Turnpoints.Select(t => t.Index == 2 ? t with { Radius = 600 } : t).ToList() };
        Assert.Equal(a.Hash, FplParser.Parse(Samples.Read("condor-task.fpl")).Hash);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void ProviderFollowsTheNewestFlightPlan()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new TaskSettings { Mode = TaskMode.Newest, FlightPlanFolder = dir.FullName };
            var provider = new TaskProvider(settings, "C:\\Condor3", (_, _) => new FakeConverter());
            Assert.False(provider.Poll());
            Assert.Null(provider.Current);

            var old = Path.Combine(dir.FullName, "a.fpl");
            File.WriteAllText(old, Samples.Read("condor-task.fpl"));
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-5));
            Assert.True(provider.Poll());
            Assert.Equal("Testaufgabe Muster", provider.Current!.Name);
            Assert.True(provider.Current.HasCoordinates);
            Assert.False(provider.Poll()); // unchanged

            Directory.CreateDirectory(Path.Combine(dir.FullName, "User"));
            var newer = Path.Combine(dir.FullName, "User", "b.fpl");
            File.WriteAllText(newer, Samples.Read("condor-task.fpl").Replace("Testaufgabe Muster", "Neue Aufgabe"));
            Assert.True(provider.Poll());
            Assert.Equal("Neue Aufgabe", provider.Current!.Name);

            File.WriteAllText(newer, "kaputt");
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(1));
            Assert.True(provider.Poll());
            Assert.True(provider.HasError);
            Assert.Null(provider.Current);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
