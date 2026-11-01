using System.Text.Json;

namespace BubbleBot.Cli.Repository;

/// <summary>
/// Read-only route preview over the map neighbour metadata. The user can load
/// BubbleBot's local Data/maps.json; no 98 MB map file is bundled in the desktop package.
/// </summary>
public static class DesktopMapRouteCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<long, MapRouteRecord> _maps = new Dictionary<long, MapRouteRecord>();
    private static string? _loadError = "Select BubbleBot Data/maps.json to load the local map graph.";

    public static bool IsAvailable => _maps.Count > 0;
    public static string? LoadError => _loadError;
    public static int MapCount => _maps.Count;

    public static bool LoadFromFile(string path, out string error)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Map data file was not found.", path);

            using var stream = File.OpenRead(path);
            var records = JsonSerializer.Deserialize<List<MapRouteRecord>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var result = records?.Where(x => x.Id > 0).GroupBy(x => x.Id)
                .ToDictionary(x => x.Key, x => x.First()) ?? new Dictionary<long, MapRouteRecord>();
            if (result.Count == 0)
                throw new InvalidDataException("The file contains no usable map records.");

            lock (Sync)
            {
                _maps = result;
                _loadError = null;
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException )
        {
            error = exception.Message;
            lock (Sync) _loadError = $"Map catalog load failed: {exception.Message}";
            return false;
        }
    }

    public static IReadOnlyList<RouteMapStep> FindRoute(long fromMapId, long toMapId, int maxVisited = 250_000)
    {
        IReadOnlyDictionary<long, MapRouteRecord> maps;
        lock (Sync) maps = _maps;
        if (maps.Count == 0)
            throw new InvalidOperationException(_loadError ?? "Map catalog is not loaded.");
        if (!maps.ContainsKey(fromMapId) || !maps.ContainsKey(toMapId))
            throw new KeyNotFoundException("Start or destination map ID is not present in the loaded map catalog.");
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

            foreach (var neighbour in map.Neighbours)
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
            throw new InvalidOperationException("Route preview reached its safety limit; verify the selected map IDs.");
        return [];
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

    private sealed class MapRouteRecord
    {
        public MapRouteRecord() { }
        public long Id { get; set; }
        public long TopNeighbourId { get; set; }
        public long BottomNeighbourId { get; set; }
        public long LeftNeighbourId { get; set; }
        public long RightNeighbourId { get; set; }
        public IEnumerable<long> Neighbours =>
            [TopNeighbourId, BottomNeighbourId, LeftNeighbourId, RightNeighbourId];
    }
}

public sealed record RouteMapStep(long MapId, string StepType);
