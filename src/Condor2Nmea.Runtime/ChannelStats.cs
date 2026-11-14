namespace Condor2Nmea.Runtime;

public enum ChannelState { Stopped, Waiting, Active, Error }

/// <summary>Thread-safe statistics for a source or output (rate, last message, errors).</summary>
public sealed class ChannelStats
{
    private readonly object _lock = new();
    private readonly Queue<DateTime> _recent = new();
    private long _count;
    private long _errors;
    private DateTime _last;
    private ChannelState _state = ChannelState.Stopped;
    private string _message = "";

    public string Name { get; }

    public ChannelStats(string name) => Name = name;

    public void OnMessage(DateTime now)
    {
        lock (_lock)
        {
            _count++;
            _last = now;
            _recent.Enqueue(now);
            while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromSeconds(5))
                _recent.Dequeue();
            if (_recent.Count > 20000)
                _recent.Dequeue();
            if (_state != ChannelState.Active)
            {
                _state = ChannelState.Active;
                _message = "";
            }
        }
    }

    public void OnError(string message)
    {
        lock (_lock)
        {
            _errors++;
            _message = message;
        }
    }

    public void SetState(ChannelState state, string message = "")
    {
        lock (_lock)
        {
            _state = state;
            _message = message;
        }
    }

    public ChannelSnapshot Snapshot(DateTime now)
    {
        lock (_lock)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > TimeSpan.FromSeconds(5))
                _recent.Dequeue();
            var state = _state;
            // no data for a while → back to waiting
            if (state == ChannelState.Active && _last != default && now - _last > TimeSpan.FromSeconds(5))
                state = ChannelState.Waiting;
            return new ChannelSnapshot(Name, state, _message, _count, _errors,
                _last == default ? null : now - _last, _recent.Count / 5.0);
        }
    }
}

public sealed record ChannelSnapshot(
    string Name, ChannelState State, string Message, long Count, long Errors, TimeSpan? Age, double RateHz);
