using System.Runtime.InteropServices;
using Condor2Nmea.Core.Geo;
using Condor2Nmea.Core.Tasks;

namespace Condor2Nmea.Runtime.Tasks;

/// <summary>
/// Converts Condor landscape coordinates with Condor's own NaviCon.dll (in the Condor folder):
/// NaviConInit(trnPath), XYToLat(x, y), XYToLon(x, y) – the same calls Condor2Nav and pycondor use.
/// </summary>
public sealed class NaviConConverter : ICondorCoordinateConverter, IDisposable
{
    private static readonly object Lock = new(); // the DLL keeps one global landscape

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int NaviConInitFn([MarshalAs(UnmanagedType.LPStr)] string trnFile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate float XyFn(float x, float y);

    private readonly IntPtr _lib;
    private readonly XyFn _toLat;
    private readonly XyFn _toLon;

    public string Landscape { get; }

    public NaviConConverter(string condorPath, string landscape)
    {
        var dll = Path.Combine(condorPath, "NaviCon.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException($"NaviCon.dll nicht gefunden in „{condorPath}“. Ist der Condor-Ordner richtig eingestellt?", dll);

        var machine = PeMachine(dll);
        var is64 = Environment.Is64BitProcess;
        if (machine == 0x014C && is64 || machine == 0x8664 && !is64)
            throw new BadImageFormatException(
                $"Condors NaviCon.dll ist {(machine == 0x014C ? "32" : "64")}-Bit, Condor2NMEA läuft als {(is64 ? "64" : "32")}-Bit. " +
                "Condor2NMEA wird als 32-Bit-Programm ausgeliefert, passend zu Condor – bitte die 32-Bit-Version (win-x86) verwenden.");

        var trn = FindTrn(condorPath, landscape)
                  ?? throw new FileNotFoundException($"Landschaft „{landscape}“ nicht gefunden (…\\Landscapes\\{landscape}\\{landscape}.trn).");

        _lib = NativeLibrary.Load(dll);
        try
        {
            var init = Marshal.GetDelegateForFunctionPointer<NaviConInitFn>(NativeLibrary.GetExport(_lib, "NaviConInit"));
            _toLat = Marshal.GetDelegateForFunctionPointer<XyFn>(NativeLibrary.GetExport(_lib, "XYToLat"));
            _toLon = Marshal.GetDelegateForFunctionPointer<XyFn>(NativeLibrary.GetExport(_lib, "XYToLon"));
            lock (Lock)
                init(trn);
        }
        catch
        {
            NativeLibrary.Free(_lib);
            throw;
        }
        Landscape = landscape;
    }

    public GeoPoint ToGeo(double x, double y)
    {
        lock (Lock)
        {
            var lat = _toLat((float)x, (float)y);
            var lon = _toLon((float)x, (float)y);
            var p = new GeoPoint(lat, lon);
            if (!p.IsValid || lat == 0 && lon == 0)
                throw new InvalidOperationException($"NaviCon.dll lieferte keine gültige Position für X={x}, Y={y}.");
            return p;
        }
    }

    public void Dispose()
    {
        if (_lib != IntPtr.Zero)
            NativeLibrary.Free(_lib);
    }

    public static string? FindTrn(string condorPath, string landscape)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in new[] { condorPath, Path.Combine(docs, "Condor3"), Path.Combine(docs, "Condor") })
        {
            var p = Path.Combine(root, "Landscapes", landscape, landscape + ".trn");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>IMAGE_FILE_HEADER.Machine of a PE file (0x014C = x86, 0x8664 = x64), 0 if unreadable.</summary>
    public static int PeMachine(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            fs.Position = 0x3C;
            var peOffset = br.ReadInt32();
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) // "PE\0\0"
                return 0;
            return br.ReadUInt16();
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
