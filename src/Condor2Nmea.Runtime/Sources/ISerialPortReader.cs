namespace Condor2Nmea.Runtime.Sources;

/// <summary>Minimal blocking serial port abstraction.</summary>
public interface ISerialPortReader : IDisposable
{
    /// <summary>Reads available bytes, returns 0 on timeout.</summary>
    int Read(byte[] buffer);
}

public static class SerialPortFactory
{
    public static ISerialPortReader Open(string portName, int baudRate)
    {
        if (OperatingSystem.IsWindows())
            return new Win32SerialPort(portName, baudRate);
        return new DotNetSerialPort(portName, baudRate);
    }
}

/// <summary>
/// Fallback for non-Windows systems (development, tests with socat/pty).
/// System.IO.Ports on Windows only accepts names starting with "COM", which is why
/// <see cref="Win32SerialPort"/> is used there (com0com's internal ports are called "CNCB0").
/// </summary>
internal sealed class DotNetSerialPort : ISerialPortReader
{
    private readonly System.IO.Ports.SerialPort _port;

    public DotNetSerialPort(string name, int baud)
    {
        _port = new System.IO.Ports.SerialPort(name, baud) { ReadTimeout = 500, DtrEnable = true, RtsEnable = true };
        _port.Open();
    }

    public int Read(byte[] buffer)
    {
        try { return _port.Read(buffer, 0, buffer.Length); }
        catch (TimeoutException) { return 0; }
    }

    public void Dispose() => _port.Dispose();
}
