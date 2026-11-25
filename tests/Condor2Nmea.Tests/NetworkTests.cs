using System.Net;
using Condor2Nmea.Core.Network;

namespace Condor2Nmea.Tests;

public class NetworkTests
{
    private static AdapterAddress A(string name, string desc, AdapterKind kind, bool gw, string ip) =>
        new(name, desc, kind, gw, IPAddress.Parse(ip));

    [Fact]
    public void HamachiIsNotThePrimaryAddress()
    {
        var ranked = LocalAddressRanker.Rank(new[]
        {
            A("Hamachi", "LogMeIn Hamachi Virtual Ethernet Adapter", AdapterKind.Ethernet, true, "25.12.34.56"),
            A("WLAN", "Intel(R) Wi-Fi 6 AX201", AdapterKind.Wlan, true, "192.168.178.23"),
        });
        Assert.Equal("192.168.178.23", ranked[0].Address.ToString());
        Assert.Equal(AdapterKind.Virtual, ranked[1].Kind);
    }

    [Fact]
    public void VpnRangeIsVirtualEvenWithNeutralName()
    {
        var ranked = LocalAddressRanker.Rank(new[]
        {
            A("Ethernet 3", "Ethernet Adapter", AdapterKind.Ethernet, true, "25.1.2.3"),
            A("Ethernet", "Realtek PCIe GbE", AdapterKind.Ethernet, true, "10.0.0.5"),
        });
        Assert.Equal("10.0.0.5", ranked[0].Address.ToString());
        Assert.Equal(AdapterKind.Virtual, ranked[1].Kind);
    }

    [Fact]
    public void HyperVSwitchGoesBehindWlan()
    {
        var ranked = LocalAddressRanker.Rank(new[]
        {
            A("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", AdapterKind.Ethernet, false, "172.20.112.1"),
            A("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter #2", AdapterKind.Ethernet, true, "172.30.0.1"),
            A("WLAN", "Wi-Fi", AdapterKind.Wlan, true, "192.168.1.40"),
            A("Ethernet", "Realtek", AdapterKind.Ethernet, false, "169.254.10.1"),
        });
        Assert.Equal(new[] { "192.168.1.40", "172.20.112.1", "172.30.0.1" }, ranked.Select(r => r.Address.ToString()));
    }

    [Fact]
    public void AdapterWithGatewayWins()
    {
        var ranked = LocalAddressRanker.Rank(new[]
        {
            A("Ethernet", "Realtek", AdapterKind.Ethernet, false, "192.168.50.2"),
            A("WLAN", "Wi-Fi", AdapterKind.Wlan, true, "192.168.0.17"),
        });
        Assert.Equal("192.168.0.17", ranked[0].Address.ToString());
    }
}
