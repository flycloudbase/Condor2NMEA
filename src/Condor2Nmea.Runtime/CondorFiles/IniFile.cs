namespace Condor2Nmea.Runtime.CondorFiles;

/// <summary>Minimal INI reader/writer that keeps unknown lines, comments and order intact.</summary>
public sealed class IniFile
{
    private readonly List<string> _lines;

    public IniFile(IEnumerable<string> lines) => _lines = lines.ToList();

    public static IniFile Load(string path) =>
        new(File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>());

    public IReadOnlyList<string> Lines => _lines;

    public string? Get(string section, string key)
    {
        var (start, end) = FindSection(section);
        if (start < 0)
            return null;
        for (var i = start + 1; i < end; i++)
            if (TryKey(_lines[i], out var k, out var v) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    public void Set(string section, string key, string value)
    {
        var (start, end) = FindSection(section);
        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0)
                _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }
        for (var i = start + 1; i < end; i++)
        {
            if (TryKey(_lines[i], out var k, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = $"{k}={value}";
                return;
            }
        }
        // insert after the last non-empty line of the section
        var insert = end;
        while (insert > start + 1 && _lines[insert - 1].Trim().Length == 0)
            insert--;
        _lines.Insert(insert, $"{key}={value}");
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, _lines);
    }

    private (int Start, int End) FindSection(string section)
    {
        var start = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            var t = _lines[i].Trim();
            if (!t.StartsWith('[') || !t.EndsWith(']'))
                continue;
            if (start >= 0)
                return (start, i);
            if (t[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
                start = i;
        }
        return (start, _lines.Count);
    }

    private static bool TryKey(string line, out string key, out string value)
    {
        key = value = "";
        var t = line.Trim();
        if (t.Length == 0 || t[0] is ';' or '#' or '[')
            return false;
        var eq = t.IndexOf('=');
        if (eq <= 0)
            return false;
        key = t[..eq].Trim();
        value = t[(eq + 1)..].Trim();
        return true;
    }
}
