using System.Collections.Generic;
using System.Linq;

namespace BubbleBot.Cli.Services.Clients;

/// <summary>
/// Explicitly enabled, in-memory packet payload observer for this client's decoded
/// application messages. It never records authentication, account/session, or chat
/// message types and has strict per-entry and total-memory bounds.
/// </summary>
public sealed class PacketCaptureStore
{
    private const int MaxEntries = 200;
    private const int MaxPayloadBytes = 4096;
    private const int MaxTotalPayloadBytes = 1024 * 1024;
    private static readonly string[] ExcludedTypeFragments =
    [
        "auth", "login", "token", "credential", "password", "account",
        "session", "chat", "private", "hello", "handshake", "identification"
    ];

    private readonly object _sync = new();
    private readonly Queue<PacketCaptureEntry> _entries = new();
    private bool _enabled;
    private int _storedPayloadBytes;
    private long _nextId;

    public bool IsEnabled { get { lock (_sync) return _enabled; } }

    public void Start()
    {
        lock (_sync) _enabled = true;
    }

    public void Stop()
    {
        lock (_sync) _enabled = false;
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _storedPayloadBytes = 0;
        }
    }

    public void TryCapture(string direction, string? typeName, byte[]? payload)
    {
        if (payload is null || payload.Length == 0 || string.IsNullOrWhiteSpace(typeName))
            return;

        var normalized = typeName.ToLowerInvariant();
        if (ExcludedTypeFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
            return;

        lock (_sync)
        {
            if (!_enabled) return;

            var length = Math.Min(payload.Length, MaxPayloadBytes);
            var copy = payload.AsSpan(0, length).ToArray();
            var entry = new PacketCaptureEntry(
                ++_nextId,
                DateTimeOffset.UtcNow,
                direction,
                typeName,
                payload.Length,
                payload.Length > length,
                Convert.ToHexString(copy),
                length);

            _entries.Enqueue(entry);
            _storedPayloadBytes += length;

            while (_entries.Count > MaxEntries || _storedPayloadBytes > MaxTotalPayloadBytes)
            {
                var removed = _entries.Dequeue();
                _storedPayloadBytes -= removed.StoredBytes;
            }
        }
    }

    public PacketCaptureSnapshot Snapshot()
    {
        lock (_sync)
            return new PacketCaptureSnapshot(_enabled, _entries.Count, _storedPayloadBytes, _entries.Reverse().ToArray());
    }
}

public sealed record PacketCaptureEntry(
    long Id,
    DateTimeOffset Timestamp,
    string Direction,
    string Type,
    int OriginalBytes,
    bool Truncated,
    string PayloadHex,
    int StoredBytes);

public sealed record PacketCaptureSnapshot(
    bool Enabled,
    int Count,
    int StoredPayloadBytes,
    IReadOnlyList<PacketCaptureEntry> Entries);
