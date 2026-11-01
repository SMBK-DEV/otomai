using System.Text.Json;
using BubbleBot.Cli.Repository.Maps;

namespace BubbleBot.Cli.Repository;

/// <summary>
/// Read-only route preview over the checked-in map neighbour graph.
/// This graph covers cardinal map neighbours only; it does not model zaaps,
/// dungeon entrances, doors, quest gates or character-specific criteria.
/// </summary>
public static class DesktopMapRouteCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<long, MapRecord>> Maps = new(Load);
    private static string? _loadError;

    public static bool IsAvailable => Maps.Value.Count > 0;
    public static string? LoadError => _loadError;
    public static int MapCount => Maps.Value.Count;

    public static IReadOnlyList<RouteMapStep> FindRoute(long fromMapId, long toMapId, int maxVisited = 250_000)
    {
        var maps = Maps.Value;
        if (!maps.ContainsKey(fromMapId) || !maps.ContainsKey(toMapId))
            throw new KeyNotFoundException("Start or destination map ID is not present in the local map catalog.");
        if (fromMapId == toMapId)
            return [new RouteMapStep(fromMapId, "Start and destination")];

        var queue = new Queue<long>();
        var previous = new Dictionary<long, long>();
        var visited = new HashSet<long> { fromMapId };
        queue.Enqueue(fromMapId);

        while (queue.Count > 0 && visited.Count <= maxVisited)
        {
            var current = queue.Dequeue();
            if (!maps.TryGetValue(current, out var map)) continue;

            foreach (var neighbour in GetNeighbours(map))
            {
                if (neighbour <= 0 || !maps.ContainsKey(neighbour) || !visited.Add(neighbour))
                    continue;
                previous[neighbour] = current;
                if (neighbour == toMapId)
                    return Reconstruct(previous, fromMapId, toMapId);
                queue.Enqueue(neighbour);
            }
        }

        if (visited.Count > maxVisited)
            throw new InvalidOperationException("Route preview reached its safety limit; narrow the map range or verify the map data.");
        return [];
    }

    private static IReadOnlyDictionary<long, MapRecord> Load()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "maps.json"),
            Path.Combine(Environment.CurrentDirectory, "Data", "maps.json"),
            Path.Combine(AppContext.BaseDirectory, "maps.json")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            _loadError = "Data/maps.json was not found beside the desktop application.";
            return new Dictionary<long, MapRecord>();
        }

        try
        {
            var records = JsonSerializer.Deserialize<List<MapRecord>>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var result = records?.Where(x => x.Id > 0).GroupBy(x => x.Id)
                .ToDictionary(x => x.Key, x => x.First()) ?? new Dictionary<long, MapRecord>();
            if (result.Count == 0) _loadError = "The local map catalog is empty or could not be parsed.";
            return result;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _loadError = $"Map catalog load failed: {exception.Message}";
            return new Dictionary<long, MapRecord>();
        }
    }

    private static IEnumerable<long> GetNeighbours(MapRecord map)
    {
        yield return map.TopNeighbourId;
        yield return map.BottomNeighbourId;
        yield return map.LeftNeighbourId;
        yield return map.RightNeighbourId;
    }

    private static IReadOnlyList<RouteMapStep> Reconstruct(
        IReadOnlyDictionary<long, long> previous, long start, long destination)
    {
        var path = new List<long> { destination };
        var current = destination;
        while (current != start)
        {
            if (!previous.TryGetValue(current, out current))
                return [];
            path.Add(current);
        }
        path.Reverse();
        return path.Select((id, index) => new RouteMapStep(id,
            index == 0 ? "Start" : index == path.Count - 1 ? "Destination" : "Cardinal neighbour")).ToArray();
    }
}

public sealed record RouteMapStep(long MapId, string StepType);
