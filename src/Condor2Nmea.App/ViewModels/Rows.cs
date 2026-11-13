namespace Condor2Nmea.App.ViewModels;

public sealed record ChannelRow(string Kind, string Name, string State, string Rate, string Count, string Age, string Message);

public sealed record LiveValue(string Name, string Value, string Source);

public sealed record TrafficRow(string Cn, string Pilot, string Plane, string Distance, string Bearing, string Vertical,
    string Speed, string Vario, string Status);

public sealed record MonitorRow(string Time, string Direction, string Channel, string Text);
