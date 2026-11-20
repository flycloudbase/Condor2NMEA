using Condor2Nmea.Core.Config;
using Condor2Nmea.Core.Tasks;

namespace Condor2Nmea.Runtime.Tasks;

/// <summary>
/// Keeps the current Condor task: either a chosen .fpl file or the newest .fpl in the flight plan folder.
/// Re-reads it whenever the file changes and converts the turnpoints to WGS84 with NaviCon.dll.
/// </summary>
public sealed class TaskProvider
{
    private readonly TaskSettings _settings;
    private readonly string _condorPath;
    private readonly Func<string, string, ICondorCoordinateConverter> _converterFactory;
    private string? _loadedPath;
    private DateTime _loadedWrite;
    private ICondorCoordinateConverter? _converter;
    private string _converterLandscape = "";

    public TaskProvider(TaskSettings settings, string condorPath,
        Func<string, string, ICondorCoordinateConverter>? converterFactory = null)
    {
        _settings = settings;
        _condorPath = condorPath;
        _converterFactory = converterFactory ?? ((path, landscape) => new NaviConConverter(path, landscape));
    }

    /// <summary>Current task with coordinates, or null.</summary>
    public CondorTask? Current { get; private set; }

    /// <summary>Human readable state for the UI.</summary>
    public string Status { get; private set; } = "Keine Aufgabe.";

    public bool HasError { get; private set; }

    public static string DefaultFlightPlanFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Condor3", "FlightPlans");

    /// <summary>Checks for a new or changed task. Returns true when <see cref="Current"/> changed.</summary>
    public bool Poll()
    {
        if (_settings.Mode == TaskMode.Off)
        {
            var changed = Current is not null;
            Current = null;
            Status = "Aufgabe senden ist ausgeschaltet.";
            HasError = false;
            return changed;
        }

        var path = _settings.Mode == TaskMode.File ? _settings.FilePath : NewestFlightPlan();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = _settings.Mode == TaskMode.File
                ? "Die gewählte Aufgabendatei existiert nicht."
                : $"Keine Aufgabe (.fpl) in „{Folder}“ gefunden.";
            HasError = _settings.Mode == TaskMode.File;
            var changed = Current is not null;
            Current = null;
            _loadedPath = null;
            return changed;
        }

        var write = File.GetLastWriteTimeUtc(path);
        if (path == _loadedPath && write == _loadedWrite)
            return false;

        try
        {
            var task = FplParser.Parse(File.ReadAllText(path), path);
            if (_converter is null || !string.Equals(_converterLandscape, task.Landscape, StringComparison.OrdinalIgnoreCase))
            {
                (_converter as IDisposable)?.Dispose();
                _converter = _converterFactory(_condorPath, task.Landscape);
                _converterLandscape = task.Landscape;
            }
            Current = task.WithCoordinates(_converter);
            _loadedPath = path;
            _loadedWrite = write;
            HasError = false;
            Status = $"„{Current.Name}“ ({Current.Landscape}), {Current.Turnpoints.Count - 1} Punkte, " +
                     $"{Current.Distance / 1000:0.0} km – aus {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            // keep retrying on the next change of the file, but do not hammer a broken file every poll
            _loadedPath = path;
            _loadedWrite = write;
            Current = null;
            HasError = true;
            Status = $"Aufgabe „{Path.GetFileName(path)}“ konnte nicht gelesen werden: {ex.Message}";
            return true;
        }
    }

    private string Folder => string.IsNullOrWhiteSpace(_settings.FlightPlanFolder) ? DefaultFlightPlanFolder : _settings.FlightPlanFolder;

    private string? NewestFlightPlan()
    {
        try
        {
            if (!Directory.Exists(Folder))
                return null;
            return new DirectoryInfo(Folder).EnumerateFiles("*.fpl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
