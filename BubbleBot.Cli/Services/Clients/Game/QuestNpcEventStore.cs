using System.Collections;
using System.Reflection;
using Bubble.Shared.Protocol;
using ProtoBuf;

namespace BubbleBot.Cli.Services.Clients.Game;

/// <summary>
/// Bounded, in-memory observer for verified quest/NPC protocol events.
/// Keeps numeric/enum metadata only; free-form strings and byte payloads are omitted.
/// Field names are schema labels and may be obfuscated by the game protocol.
/// </summary>
public sealed class QuestNpcEventStore
{
    private const int MaxEvents = 100;
    private const int MaxFieldsPerEvent = 32;
    private readonly object _sync = new();
    private readonly Queue<QuestNpcEventSnapshot> _events = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public void Observe(IProtoMessage message)
    {
        if (message is null) return;
        var fields = new List<QuestNpcFieldSnapshot>();
        AppendFields(message, string.Empty, 0, fields);
        var type = message.GetType().Name;
        var snapshot = new QuestNpcEventSnapshot(DateTimeOffset.UtcNow, type, fields);

        lock (_sync)
        {
            _events.Enqueue(snapshot);
            _counts[type] = _counts.TryGetValue(type, out var count) ? count + 1 : 1;
            while (_events.Count > MaxEvents) _events.Dequeue();
        }
    }

    public QuestNpcEventStoreSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new QuestNpcEventStoreSnapshot(
                _events.Reverse().ToArray(),
                _counts.OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new QuestNpcEventCount(pair.Key, pair.Value))
                    .ToArray());
        }
    }

    private static void AppendFields(object value, string prefix, int depth, List<QuestNpcFieldSnapshot> output)
    {
        if (depth > 2 || output.Count >= MaxFieldsPerEvent) return;

        var properties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ProtoMemberAttribute>()))
            .Where(item => item.Attribute is not null && item.Property.CanRead && item.Property.GetIndexParameters().Length == 0)
            .OrderBy(item => item.Attribute!.Tag);

        foreach (var item in properties)
        {
            if (output.Count >= MaxFieldsPerEvent) break;
            var property = item.Property;
            var attribute = item.Attribute!;
            var name = string.IsNullOrWhiteSpace(attribute.Name) ? property.Name : attribute.Name;
            var path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";

            object? field;
            try { field = property.GetValue(value); }
            catch { continue; }
            if (field is null) continue;

            if (field is string text)
            {
                output.Add(new QuestNpcFieldSnapshot(attribute.Tag, path, "string", $"[omitted; {text.Length} chars]"));
                continue;
            }
            if (field is byte[] bytes)
            {
                output.Add(new QuestNpcFieldSnapshot(attribute.Tag, path, "bytes", $"[omitted; {bytes.Length} bytes]"));
                continue;
            }
            if (field.GetType().IsEnum)
            {
                output.Add(new QuestNpcFieldSnapshot(attribute.Tag, path, "enum",
                    $"{field} ({Convert.ToInt64(field, System.Globalization.CultureInfo.InvariantCulture)})"));
                continue;
            }
            if (IsScalar(field))
            {
                output.Add(new QuestNpcFieldSnapshot(attribute.Tag, path, field.GetType().Name,
                    Convert.ToString(field, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                continue;
            }
            if (field is IEnumerable sequence)
            {
                var index = 0;
                foreach (var child in sequence)
                {
                    if (index >= 8 || output.Count >= MaxFieldsPerEvent) break;
                    if (child is not null)
                    {
                        var childPath = $"{path}[{index}]";
                        if (child is string childText)
                            output.Add(new QuestNpcFieldSnapshot(attribute.Tag, childPath, "string", $"[omitted; {childText.Length} chars]"));
                        else if (child.GetType().IsEnum)
                            output.Add(new QuestNpcFieldSnapshot(attribute.Tag, childPath, "enum",
                                $"{child} ({Convert.ToInt64(child, System.Globalization.CultureInfo.InvariantCulture)})"));
                        else if (IsScalar(child))
                            output.Add(new QuestNpcFieldSnapshot(attribute.Tag, childPath, child.GetType().Name,
                                Convert.ToString(child, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                        else
                            AppendFields(child, childPath, depth + 1, output);
                    }
                    index++;
                }
                continue;
            }
            AppendFields(field, path, depth + 1, output);
        }
    }

    private static bool IsScalar(object value) =>
        value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

public sealed record QuestNpcFieldSnapshot(int Number, string Path, string Kind, string Value);
public sealed record QuestNpcEventSnapshot(DateTimeOffset Timestamp, string Type, IReadOnlyList<QuestNpcFieldSnapshot> Fields);
public sealed record QuestNpcEventCount(string Type, int Count);
public sealed record QuestNpcEventStoreSnapshot(IReadOnlyList<QuestNpcEventSnapshot> Events, IReadOnlyList<QuestNpcEventCount> Counts);
