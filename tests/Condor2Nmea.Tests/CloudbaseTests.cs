using Condor2Nmea.Core.Config;
using Condor2Nmea.Core.Generation;
using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Tests;

public class CloudbaseTests
{
    private static FlightSnapshot FlyingSnapshot()
    {
        var state = new FlightState();
        var now = DateTime.UtcNow;
        new CondorNmeaParser().Apply(NmeaLine.TryParse("$LXWP0,Y,299.4,2912.3,-4.17,,,,,,98,340,19.4*52")!, state, now);
        UdpPacketParser.Apply(
            "time=13.25\nairspeed=63.5\nvario=-3.9\nevario=-4.5\nnettovario=-0.85\nintegrator=1.2\ncompass=98\n" +
            "bank=-0.5236\npitch=0.05\ngforce=1.6\nheight=812.4\nwheelheight=811.9\nturbulencestrength=0.37\n" +
            "MC=1.5\nwater=120\nflaps=3\nradiofrequency=123.5\nhudmessages=M.Muster found an outstanding lift;",
            state, now);
        return state.Snapshot(now);
    }

    [Fact]
    public void DataSentenceCarriesAllCondorValues()
    {
        var line = CloudbaseSentences.Data(FlyingSnapshot())!;
        Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(line));
        Assert.True(line.Length <= 256, "Cloudbase rejects sentences longer than 256 characters");
        var f = NmeaLine.TryParse(line)!;
        Assert.Equal("PCBC", f.Type);
        Assert.Equal("DATA", f[0]);
        Assert.Equal("1", f[1]);
        Assert.Equal("131500.00", f[2]);  // sim time 13.25 h
        Assert.Equal("63.5", f[3]);       // IAS
        Assert.Equal("83.2", f[4]);       // TAS 299.4 km/h
        Assert.Equal("-4.17", f[5]);      // TE from LXWP0
        Assert.Equal("-0.85", f[6]);      // netto
        Assert.Equal("-3.90", f[7]);
        Assert.Equal("-4.50", f[8]);
        Assert.Equal("1.20", f[9]);
        Assert.Equal("98", f[10]);
        Assert.Equal("30.0", f[11]);      // Condor bank -0.5236 rad → +30° (right wing down)
        Assert.Equal("2.9", f[12]);
        Assert.Equal("1.60", f[13]);
        Assert.Equal("812.4", f[14]);
        Assert.Equal("0", f[15]);         // airborne
        Assert.Equal("1.5", f[16]);
        Assert.Equal("120", f[17]);
        Assert.Equal("3", f[18]);
        Assert.Equal("0.37", f[19]);
        Assert.Equal("123.500", f[20]);
        Assert.Equal(21, f.Fields.Length);
    }

    [Fact]
    public void UnknownValuesStayEmptyAndNothingIsSentWithoutData()
    {
        Assert.Null(CloudbaseSentences.Data(new FlightState().Snapshot(DateTime.UtcNow)));
        var state = new FlightState();
        var now = DateTime.UtcNow;
        UdpPacketParser.Apply("nettovario=0.5\nwheelheight=0.05", state, now);
        var f = NmeaLine.TryParse(CloudbaseSentences.Data(state.Snapshot(now))!)!;
        Assert.Equal("", f[3]);
        Assert.Equal("0.50", f[6]);
        Assert.Equal("1", f[15]); // on ground
    }

    [Fact]
    public void InfoSentenceDescribesOwnGliderAndRace()
    {
        var pilots = SpectateParser.TryParse(Samples.Read("condor3-spectate.json"))!;
        var line = CloudbaseSentences.Info(pilots[0], pilots.Count, "Condor2NMEA 0.1.0");
        Assert.Equal(NmeaChecksum.Result.Valid, NmeaChecksum.Verify(line));
        var f = NmeaLine.TryParse(line)!;
        Assert.Equal(new[] { "INFO", "1", "Condor", "Condor2NMEA 0.1.0", "MM", "D-KXMM", "Ventus3-18", "Max Muster", "Germany",
            "Finished", "1", "999.7", "252.3", "210.7", "01:11:51", "2" }, f.Fields);

        var empty = NmeaLine.TryParse(CloudbaseSentences.Info(null, null, "x"))!;
        Assert.Equal("", empty[4]);
        Assert.Equal("", empty[15]);
    }

    [Fact]
    public void TextIsMadeNmeaSafe()
    {
        Assert.Equal("Cedric Gunther", CloudbaseSentences.Text("Cédric Günther", 30));
        Assert.Equal("Strasse 12 a", CloudbaseSentences.Text("Straße 12, a*$", 30));
        Assert.Equal("", CloudbaseSentences.Text("—", 30));
        Assert.Equal("abc", CloudbaseSentences.Text("abcdef", 3));
    }

    [Fact]
    public void EventSentence()
    {
        var f = NmeaLine.TryParse(CloudbaseSentences.Event(TimeSpan.FromHours(12.5), "M.Muster released"))!;
        Assert.Equal(new[] { "EVENT", "1", "123000.00", "M.Muster released" }, f.Fields);
    }

    [Fact]
    public void CloudbasePresetSendsEverything()
    {
        var p = OutputProfile.FromPreset(ProfilePreset.Cloudbase);
        Assert.True(p.Gps && p.Lxwp0 && p.Flarm && p.FlarmNames && p.Cloudbase);
        Assert.False(p.RawPassthrough);
    }
}
