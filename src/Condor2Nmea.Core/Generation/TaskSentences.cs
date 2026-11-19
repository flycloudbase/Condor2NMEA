using Condor2Nmea.Core.Nmea;
using Condor2Nmea.Core.Tasks;

namespace Condor2Nmea.Core.Generation;

/// <summary>
/// The Condor task for Cloudbase as <c>$PCBC,TASK</c> (header) followed by one <c>$PCBC,TP</c> per turnpoint.
/// Specification: docs/PCBC.md. The block is repeated regularly; receivers assemble it by task hash and replace
/// the previous task once all turnpoints of a new hash have arrived.
/// </summary>
public static class TaskSentences
{
    public const int Version = 1;

    public static IReadOnlyList<string> Build(CondorTask task)
    {
        if (!task.HasCoordinates)
            return Array.Empty<string>();
        var hash = task.Hash;
        var n = task.Turnpoints.Count;
        var lines = new List<string>(n + 1)
        {
            NmeaChecksum.Wrap(string.Join(',', "PCBC", "TASK", Version.ToString(Inv.C), hash, n.ToString(Inv.C),
                Inv.F(task.Distance / 1000, 1), CloudbaseSentences.Text(task.Landscape, 30), CloudbaseSentences.Text(task.Name, 50))),
        };
        foreach (var t in task.Turnpoints)
        {
            var p = t.Position!.Value;
            lines.Add(NmeaChecksum.Wrap(string.Join(',',
                "PCBC", "TP", Version.ToString(Inv.C), hash,
                t.Index.ToString(Inv.C), n.ToString(Inv.C),
                Role(t.Role),
                Inv.F(p.Latitude, 6), Inv.F(p.Longitude, 6),
                Inv.F(t.Elevation, 0),
                t.SectorType.ToString(Inv.C),
                Inv.F(t.Radius, 0), Inv.F(t.Angle, 0),
                Inv.F(t.Altitude, 0), Inv.F(t.Width, 0), Inv.F(t.Height, 0), Inv.F(t.Azimuth, 0),
                t.IsAirport ? "1" : "0",
                CloudbaseSentences.Text(t.Name, 30))));
        }
        return lines;
    }

    private static string Role(TurnpointRole r) => r switch
    {
        TurnpointRole.Takeoff => "T",
        TurnpointRole.Start => "S",
        TurnpointRole.Finish => "F",
        _ => "W",
    };
}
