using System.Collections.Concurrent;

namespace NYROX.Desktop.Control;

internal sealed class ControlLogStore
{
    private readonly ConcurrentQueue<ControlLogEntry> _entries = new();
    private long _sequence;
    private const int Capacity = 600;

    public void Write(string level, string source, string message)
    {
        var entry = new ControlLogEntry(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            level,
            source,
            message.Length > 1200 ? message[..1200] : message);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity)
            _entries.TryDequeue(out _);
    }

    public IReadOnlyList<ControlLogEntry> Read(int limit = 200)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        return _entries.ToArray().TakeLast(safeLimit).Reverse().ToArray();
    }
}

internal sealed record ControlLogEntry(long Sequence, DateTimeOffset Timestamp, string Level, string Source, string Message);
