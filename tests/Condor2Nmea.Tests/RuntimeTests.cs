using Condor2Nmea.Runtime.CondorFiles;
using Condor2Nmea.Runtime.Sources;
using Condor2Nmea.Runtime.VirtualCom;

namespace Condor2Nmea.Tests;

public class RuntimeTests
{
    [Fact]
    public void ParsesSetupcList()
    {
        const string output = """
                   CNCA0 PortName=COM9
                   CNCB0 PortName=-
                   CNCA1 PortName=COM#,RealPortName=COM12,EmuBR=yes
                   CNCB1 PortName=CNCB1
            """;
        var pairs = Com0comManager.ParseList(output);
        Assert.Equal(2, pairs.Count);
        Assert.Equal("COM9", pairs[0].CondorPort);
        Assert.Equal("CNCB0", pairs[0].AppPort);
        Assert.Equal("COM12", pairs[1].CondorPort);
        Assert.Equal("CNCB1", pairs[1].AppPort);
    }

    [Fact]
    public void SetupcScriptRunsFromTheInstallFolderAndCapturesOutput()
    {
        var script = Com0comManager.BuildScript(@"C:\Program Files (x86)\com0com", "--silent install PortName=COM9 -",
            @"C:\ProgramData\Condor2NMEA\setupc\out.txt", @"C:\ProgramData\Condor2NMEA\setupc\exit.txt");
        var lines = script.Split("\r\n");
        Assert.Contains(@"cd /d ""C:\Program Files (x86)\com0com""", lines);
        Assert.Contains(@"setupc.exe --silent install PortName=COM9 - > ""C:\ProgramData\Condor2NMEA\setupc\out.txt"" 2>&1", lines);
        Assert.Contains(@"echo %ERRORLEVEL% > ""C:\ProgramData\Condor2NMEA\setupc\exit.txt""", lines);
    }

    [Theory]
    [InlineData("3.0.0.0", true)]
    [InlineData("2.2.2.0", false)]
    [InlineData(null, false)]
    public void Com0com3IsReportedAsUnsupported(string? version, bool unsupported) =>
        Assert.Equal(unsupported, Com0comManager.IsUnsupportedVersion(version is null ? null : Version.Parse(version)));

    [Fact]
    public void RecommendsTheSigned2222Package()
    {
        Assert.Equal("https://sourceforge.net/projects/com0com/files/com0com/2.2.2.0/com0com-2.2.2.0-x64-fre-signed.zip/download",
            Com0comManager.DirectDownloadUrlX64);
        Assert.Contains("/2.2.2.0/", Com0comManager.DirectDownloadUrl);
        Assert.Contains("2.2.2.0", Com0comManager.RecommendedPackage);
    }

    [Theory]
    [InlineData("COM9", 9)]
    [InlineData("com12", 12)]
    [InlineData("CNCB0", null)]
    public void ComNumbers(string name, int? expected) => Assert.Equal(expected, ComPortRegistry.ComNumber(name));

    [Fact]
    public void IniFileKeepsUnknownContent()
    {
        var ini = new IniFile(new[] { "; comment", "[General]", "Enabled=0", "", "[Connection]", "Host=127.0.0.", "Port=55278", "", "[Other]", "X=1" });
        ini.Set("General", "Enabled", "1");
        ini.Set("Connection", "Host", "127.0.0.1");
        ini.Set("Misc", "ExtendedData1", "1");
        ini.Set("Connection", "Timeout", "5");
        Assert.Equal("1", ini.Get("General", "Enabled"));
        Assert.Equal("127.0.0.1", ini.Get("Connection", "Host"));
        Assert.Equal("5", ini.Get("Connection", "Timeout"));
        Assert.Equal("1", ini.Get("Misc", "ExtendedData1"));
        Assert.Equal("1", ini.Get("Other", "X"));
        Assert.Equal("; comment", ini.Lines[0]);
    }

    [Fact]
    public void UdpIniRecommendedSettings()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "Settings"));
            File.WriteAllText(Path.Combine(dir.FullName, "Settings", "UDP.ini"),
                "[General]\nEnabled=0\n\n[Connection]\nHost=127.0.0.\nPort=55278\n\n[Misc]\nSendIntervalMs=1\nExtendedData=0\nExtendedData1=0\nLogToFile=0\n");
            var udp = UdpIniFile.Load(dir.FullName);
            Assert.True(udp.Exists);
            Assert.False(udp.Enabled);
            udp.ApplyRecommended(55278);
            Assert.NotNull(udp.Save());
            var reloaded = UdpIniFile.Load(dir.FullName);
            Assert.True(reloaded.Enabled);
            Assert.Equal("127.0.0.1", reloaded.Host);
            Assert.True(reloaded.ExtendedData1);
            Assert.Equal(50, reloaded.SendIntervalMs);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void RecordingEscapeRoundTrips()
    {
        const string s = "a=1\r\nb=\\2\tc";
        Assert.Equal(s, RecordingFormat.Unescape(RecordingFormat.Escape(s)));
        Assert.DoesNotContain('\n', RecordingFormat.Escape(s));
    }

    [Fact]
    public void LineSplitterHandlesFragmentsAndMissingLineEnds()
    {
        var splitter = new LineSplitter();
        var a = splitter.Push("$GPRMC,1*00\r\n$LXW"u8).ToList();
        var b = splitter.Push("P0,Y*00$GPGGA,2*00\n"u8).ToList();
        Assert.Equal(new[] { "$GPRMC,1*00" }, a);
        Assert.Equal(new[] { "$LXWP0,Y*00", "$GPGGA,2*00" }, b);
    }
}
