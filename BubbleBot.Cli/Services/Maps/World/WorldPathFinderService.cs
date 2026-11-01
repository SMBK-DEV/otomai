using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using Bubble.Core.Services;
using BubbleBot.Cli.Repository.Maps;

namespace BubbleBot.Cli.Services.Maps.World;

public class WorldPathFinderService : Singleton<WorldPathFinderService>
{
    private WorldGraphEntry _worldGraph = null!;

    public void Initialize()
    {
        _worldGraph = MapRepository.Instance.GetWorldGraph();
    }

    public WorldGraphEntry GetWorldGraph()
    {
        return _worldGraph;
    }

    public void FindPath(long fromMapId,
                         int linkedZoneRp,
                         long destinationMapId,
                         Action<List<WorldGraphEdge>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var from = _worldGraph.GetVertex(fromMapId, linkedZoneRp);
        if (from == null)
        {
            callback([]);
            return;
        }

        // Keep all mutable search state local to this invocation. The service is a
        // singleton shared by clients, so request fields on the service can race.
        new PathSearchContext(_worldGraph, from, destinationMapId, callback).Next();
    }

    private sealed class PathSearchContext
    {
        private readonly WorldGraphEntry _worldGraph;
        private readonly WorldGraphVertex _from;
        private readonly long _destinationMapId;
        private readonly Action<List<WorldGraphEdge>> _callback;
        private int _linkedZone = 1;
        private bool _completed;

        public PathSearchContext(WorldGraphEntry worldGraph,
                                 WorldGraphVertex from,
                                 long destinationMapId,
                                 Action<List<WorldGraphEdge>> callback)
        {
            _worldGraph = worldGraph;
            _from = from;
            _destinationMapId = destinationMapId;
            _callback = callback;
        }

        public void Next()
        {
            if (_completed)
            {
                return;
            }

            var destination = _worldGraph.GetVertex(_destinationMapId, _linkedZone++);
            if (destination == null)
            {
                Complete([]);
                return;
            }

            var astar = new AStarService();
            astar.Initialize();
            astar.Search(_from, destination, OnAStarComplete);
        }

        private void OnAStarComplete(List<WorldGraphEdge>? path)
        {
            if (_completed)
            {
                return;
            }

            if (path == null || path.Count == 0)
            {
                Next();
                return;
            }

            Complete(path);
        }

        private void Complete(List<WorldGraphEdge> path)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _callback(path);
        }
    }
}
