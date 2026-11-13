using System.Text;

namespace Condor2Nmea.Runtime.Sources;

/// <summary>Splits a byte stream into CR/LF terminated lines.</summary>
public sealed class LineSplitter
{
    private readonly StringBuilder _buffer = new();
    private const int MaxLine = 512;

    public IEnumerable<string> Push(ReadOnlySpan<byte> data)
    {
        var lines = new List<string>();
        foreach (var b in data)
        {
            var ch = (char)b;
            if (ch is '\r' or '\n')
            {
                if (_buffer.Length > 0)
                {
                    lines.Add(_buffer.ToString());
                    _buffer.Clear();
                }
                continue;
            }
            if (ch == '$' && _buffer.Length > 0)
            {
                // a new sentence starts without line end of the previous one
                lines.Add(_buffer.ToString());
                _buffer.Clear();
            }
            if (_buffer.Length < MaxLine)
                _buffer.Append(ch);
        }
        return lines;
    }
}
