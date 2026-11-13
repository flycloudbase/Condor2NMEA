using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>
/// Serial port via Win32 API. Opens any device name ("COM5", "CNCB0", ...) as \\.\NAME.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32SerialPort : ISerialPortReader
{
    private readonly SafeFileHandle _handle;

    public Win32SerialPort(string portName, int baudRate)
    {
        var path = portName.StartsWith(@"\\.\", StringComparison.Ordinal) ? portName : @"\\.\" + portName;
        _handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new IOException($"{portName} kann nicht geöffnet werden: {new Win32Exception(err).Message}", err);
        }

        var dcb = new DCB { DCBlength = (uint)Marshal.SizeOf<DCB>() };
        if (GetCommState(_handle, ref dcb))
        {
            BuildCommDCBW($"baud={baudRate} parity=N data=8 stop=1", ref dcb);
            SetCommState(_handle, ref dcb); // com0com accepts and ignores the baud rate
        }

        var timeouts = new COMMTIMEOUTS
        {
            // return as soon as at least one byte arrived, or after 500 ms
            ReadIntervalTimeout = uint.MaxValue,
            ReadTotalTimeoutMultiplier = uint.MaxValue,
            ReadTotalTimeoutConstant = 500,
            WriteTotalTimeoutConstant = 1000,
        };
        SetCommTimeouts(_handle, ref timeouts);
        // raise DTR/RTS so the other side (Condor) sees DSR/CTS
        EscapeCommFunction(_handle, SETDTR);
        EscapeCommFunction(_handle, SETRTS);
        PurgeComm(_handle, PURGE_RXCLEAR);
    }

    public int Read(byte[] buffer)
    {
        if (!ReadFile(_handle, buffer, buffer.Length, out var read, IntPtr.Zero))
        {
            var err = Marshal.GetLastWin32Error();
            throw new IOException($"Lesefehler: {new Win32Exception(err).Message}", err);
        }
        return read;
    }

    public void Dispose() => _handle.Dispose();

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint SETRTS = 3;
    private const uint SETDTR = 5;
    private const uint PURGE_RXCLEAR = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle h, ref DCB dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle h, ref DCB dcb);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool BuildCommDCBW(string def, ref DCB dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle h, ref COMMTIMEOUTS t);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EscapeCommFunction(SafeFileHandle h, uint func);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PurgeComm(SafeFileHandle h, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int toRead, out int read, IntPtr overlapped);
}
