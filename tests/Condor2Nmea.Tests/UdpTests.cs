using Condor2Nmea.Core.Model;
using Condor2Nmea.Core.Parsing;

namespace Condor2Nmea.Tests;

public class UdpTests
{
    [Fact]
    public void ParsesRealCondor3Packets()
    {
        var packets = Samples.UdpPackets();
        Assert.True(packets.Length >= 10);
        var state = new FlightState();
        var now = DateTime.UtcNow;
        foreach (var p in packets)
            Assert.True(UdpPacketParser.Apply(p, state, now) > 20);
        var s = state.Snapshot(now);
        Assert.NotNull(s.NettoVario);
        Assert.NotNull(s.PneumaticVario);
        Assert.NotNull(s.GLoad);
        Assert.NotNull(s.Airspeed);
        Assert.Equal(123.5, s.RadioFrequency!.Value, 3);
        Assert.Equal(0, s.MacCready!.Value);
        Assert.Contains("quaternionw", s.UdpRaw.Keys);
        Assert.Contains("az", s.UdpRaw.Keys);
        Assert.Equal("M.Muster landed;", s.HudMessages);
    }

    [Fact]
    public void BankSignIsConvertedAndAltimeterNotUsedForNavigation()
    {
        var state = new FlightState();
        var now = DateTime.UtcNow;
        UdpPacketParser.Apply("time=12.5\nbank=0.5\naltitude=262.8\nevario=3.0\nvario=1.0\nMC=1.5\nwater=120\nflaps=2\n", state, now);
        var s = state.Snapshot(now);
        Assert.Equal(-0.5 * 180 / Math.PI, s.Bank!.Value, 6);
        Assert.Null(s.BaroAltitude);
        Assert.Equal(262.8, s.Altimeter!.Value, 3);
        Assert.Equal(1.0, s.TeVario!.Value); // pneumatic vario as fallback, not evario
        Assert.Equal(1.5, s.MacCready!.Value);
        Assert.Equal(120, s.WaterBallast!.Value);
        Assert.Equal(2, s.Flaps);
        Assert.Equal(TimeSpan.FromHours(12.5), s.TimeOfDay);
    }

    [Fact]
    public void NmeaWinsOverUdpWhileFresh()
    {
        var state = new FlightState { StaleAfter = TimeSpan.FromSeconds(3) };
        var parser = new CondorNmeaParser();
        var t0 = DateTime.UtcNow;
        parser.Apply(Core.Nmea.NmeaLine.TryParse("$LXWP0,Y,299.4,2912.3,-4.17,,,,,,98,340,19.4*52")!, state, t0);
        UdpPacketParser.Apply("vario=2.0\nairspeed=50", state, t0.AddSeconds(1));
        Assert.Equal(-4.17, state.Snapshot(t0.AddSeconds(1)).TeVario!.Value, 2);
        Assert.Equal(299.4 / 3.6, state.Snapshot(t0.AddSeconds(1)).TrueAirspeed!.Value, 3);

        // NMEA stops: UDP takes over after the stale time
        UdpPacketParser.Apply("vario=2.0\nairspeed=50", state, t0.AddSeconds(4));
        Assert.Equal(2.0, state.Snapshot(t0.AddSeconds(4)).TeVario!.Value, 2);
        Assert.Equal(50, state.Snapshot(t0.AddSeconds(4)).TrueAirspeed!.Value, 3);
    }
}
