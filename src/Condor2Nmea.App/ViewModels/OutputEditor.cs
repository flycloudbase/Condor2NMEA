using CommunityToolkit.Mvvm.ComponentModel;
using Condor2Nmea.Core.Config;

namespace Condor2Nmea.App.ViewModels;

/// <summary>Editable wrapper around <see cref="OutputSettings"/> with change notification.</summary>
public sealed partial class OutputEditor : ObservableObject
{
    public OutputEditor(OutputSettings settings) => Settings = settings;

    public OutputSettings Settings { get; }

    public static IReadOnlyList<Option<OutputKind>> KindOptions { get; } = new[]
    {
        new Option<OutputKind>(OutputKind.TcpServer, "TCP-Server – das Gerät verbindet sich mit diesem PC"),
        new Option<OutputKind>(OutputKind.TcpClient, "TCP-Client – dieser PC verbindet sich mit dem Gerät"),
        new Option<OutputKind>(OutputKind.Udp, "UDP – Daten an eine Adresse senden"),
    };

    public static IReadOnlyList<Option<ProfilePreset>> PresetOptions { get; } = new[]
    {
        new Option<ProfilePreset>(ProfilePreset.XCSoarCondor3, "XCSoar / LK8000 – GPS, Vario, Wind und FLARM-Verkehr"),
        new Option<ProfilePreset>(ProfilePreset.RawPlusFlarm, "Condor-NMEA unverändert + FLARM-Verkehr"),
        new Option<ProfilePreset>(ProfilePreset.Raw, "Condor-NMEA unverändert"),
        new Option<ProfilePreset>(ProfilePreset.OpenVario, "OpenVario – zweites XCSoar-Gerät für Vario/G-Last"),
        new Option<ProfilePreset>(ProfilePreset.FlarmOnly, "Nur FLARM-Verkehr"),
        new Option<ProfilePreset>(ProfilePreset.Custom, "Benutzerdefiniert"),
    };

    public string Name
    {
        get => Settings.Name;
        set { Settings.Name = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => Settings.Enabled;
        set { Settings.Enabled = value; OnPropertyChanged(); }
    }

    public OutputKind Kind
    {
        get => Settings.Kind;
        set
        {
            Settings.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsHost));
            OnPropertyChanged(nameof(HostLabel));
        }
    }

    public bool NeedsHost => Kind != OutputKind.TcpServer;
    public string HostLabel => Kind == OutputKind.TcpClient ? "IP-Adresse des Geräts" : "Ziel-Adresse";

    public string Host
    {
        get => Settings.Host;
        set { Settings.Host = value.Trim(); OnPropertyChanged(); }
    }

    public string Port
    {
        get => Settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out var p) && p is > 0 and < 65536)
                Settings.Port = p;
            OnPropertyChanged();
        }
    }

    public ProfilePreset Preset
    {
        get => Settings.Preset;
        set
        {
            if (value == ProfilePreset.Custom && Settings.Preset != ProfilePreset.Custom)
                Settings.Profile = Settings.EffectiveProfile; // start from the previous preset
            Settings.Preset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustom));
            NotifyFlags();
        }
    }

    public bool IsCustom => Preset == ProfilePreset.Custom;

    public bool RawPassthrough { get => Settings.Profile.RawPassthrough; set { Settings.Profile.RawPassthrough = value; OnPropertyChanged(); } }
    public bool Gps { get => Settings.Profile.Gps; set { Settings.Profile.Gps = value; OnPropertyChanged(); } }
    public bool Lxwp0 { get => Settings.Profile.Lxwp0; set { Settings.Profile.Lxwp0 = value; OnPropertyChanged(); } }
    public bool Lxwp2 { get => Settings.Profile.Lxwp2; set { Settings.Profile.Lxwp2 = value; OnPropertyChanged(); } }
    public bool OpenVario { get => Settings.Profile.OpenVario; set { Settings.Profile.OpenVario = value; OnPropertyChanged(); } }
    public bool Flarm { get => Settings.Profile.Flarm; set { Settings.Profile.Flarm = value; OnPropertyChanged(); } }
    public bool FlarmNames { get => Settings.Profile.FlarmNames; set { Settings.Profile.FlarmNames = value; OnPropertyChanged(); } }
    public bool WindDirectionTo { get => Settings.Profile.WindDirectionTo; set { Settings.Profile.WindDirectionTo = value; OnPropertyChanged(); } }

    private void NotifyFlags()
    {
        foreach (var n in new[] { nameof(RawPassthrough), nameof(Gps), nameof(Lxwp0), nameof(Lxwp2), nameof(OpenVario), nameof(Flarm), nameof(FlarmNames) })
            OnPropertyChanged(n);
    }
}
