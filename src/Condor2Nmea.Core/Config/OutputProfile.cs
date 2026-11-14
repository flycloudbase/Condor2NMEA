namespace Condor2Nmea.Core.Config;

public enum ProfilePreset
{
    Custom,
    /// <summary>GPS + LXWP0 + FLARM, for XCSoar driver "Condor Soaring Simulator 3", LK8000 "Condor".</summary>
    XCSoarCondor3,
    /// <summary>Condor's NMEA unchanged (like HW VSP3) + FLARM traffic.</summary>
    RawPlusFlarm,
    /// <summary>Only Condor's NMEA unchanged.</summary>
    Raw,
    /// <summary>OpenVario $POV, for a second XCSoar device with driver "OpenVario".</summary>
    OpenVario,
    /// <summary>Only FLARM traffic.</summary>
    FlarmOnly,
}

/// <summary>Which sentences an output sends.</summary>
public sealed class OutputProfile
{
    /// <summary>Forward Condor's serial NMEA lines unchanged.</summary>
    public bool RawPassthrough { get; set; }
    public bool Gps { get; set; } = true;
    public bool Lxwp0 { get; set; } = true;
    public bool Lxwp2 { get; set; }
    public bool OpenVario { get; set; }
    public bool Flarm { get; set; } = true;
    /// <summary>Send $PFLAM name messages (pilot name, CN, registration, type).</summary>
    public bool FlarmNames { get; set; } = true;
    /// <summary>LXWP0 wind as "blowing to" (XCSoar driver "Condor Soaring Simulator" for Condor 1/2).</summary>
    public bool WindDirectionTo { get; set; }

    public static OutputProfile FromPreset(ProfilePreset preset) => preset switch
    {
        ProfilePreset.XCSoarCondor3 => new() { Gps = true, Lxwp0 = true, Flarm = true, FlarmNames = true },
        ProfilePreset.RawPlusFlarm => new() { RawPassthrough = true, Gps = false, Lxwp0 = false, Flarm = true, FlarmNames = true },
        ProfilePreset.Raw => new() { RawPassthrough = true, Gps = false, Lxwp0 = false, Flarm = false, FlarmNames = false },
        ProfilePreset.OpenVario => new() { Gps = false, Lxwp0 = false, OpenVario = true, Flarm = false, FlarmNames = false },
        ProfilePreset.FlarmOnly => new() { Gps = false, Lxwp0 = false, Flarm = true, FlarmNames = true },
        _ => new(),
    };

    public OutputProfile Clone() => (OutputProfile)MemberwiseClone();
}
