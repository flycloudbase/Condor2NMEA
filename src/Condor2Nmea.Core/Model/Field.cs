namespace Condor2Nmea.Core.Model;

/// <summary>
/// One fused value. A source with lower priority only overrides a value from a higher-priority
/// source once that value has become stale.
/// </summary>
public sealed class Field<T> where T : struct
{
    private T _value;
    private DateTime _time;
    private DataSource _source;

    public bool HasValue => _source != DataSource.None;
    public DataSource Source => _source;
    public DateTime Time => _time;

    public void Update(T value, DateTime now, DataSource source, TimeSpan staleAfter)
    {
        if (_source != DataSource.None && source < _source && now - _time < staleAfter)
            return;
        _value = value;
        _time = now;
        _source = source;
    }

    public T? Get(DateTime now, TimeSpan maxAge) =>
        _source != DataSource.None && now - _time <= maxAge ? _value : null;

    public void Clear() => _source = DataSource.None;
}
