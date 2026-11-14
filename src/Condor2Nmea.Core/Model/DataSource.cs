namespace Condor2Nmea.Core.Model;

/// <summary>Origin of a value. Higher priority wins while its value is fresh.</summary>
public enum DataSource
{
    None = 0,
    Spectate = 1,
    Udp = 2,
    Nmea = 3,
}
