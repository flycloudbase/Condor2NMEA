namespace Condor2Nmea.Runtime.Sources;

/// <summary>Minimal blocking serial port abstraction.</summary>
public interface ISerialPortReader : IDisposable
{
    /// <summary>Reads available bytes, returns 0 on timeout.</summary>
    int Read(byte[] buffer);
}

public static class SerialPortFactory
{
    /// <summary>
    /// Opens a port through the Win32 API. System.IO.Ports only accepts names starting with "COM",
    /// but com0com's internal side is called "CNCB0".
    /// </summary>
    public static ISerialPortReader Open(string portName, int baudRate) => new Win32SerialPort(portName, baudRate);
}
