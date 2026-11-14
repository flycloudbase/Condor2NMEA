namespace Condor2Nmea.Runtime;

public enum MonitorDirection { In, Out, Info, Error }

/// <summary>One line for the NMEA monitor / log.</summary>
public sealed record MonitorEntry(DateTime Time, MonitorDirection Direction, string Channel, string Text);
