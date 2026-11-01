using System.Diagnostics;
using Bubble.Core.Datacenter.Datacenter.World;
using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using BubbleBot.Cli.Repository.Maps;
using Serilog;

namespace BubbleBot.Cli.Services.Maps.World;


public class AStarService
{
    private MapPositions _dest;
    private readonly Dictionary<WorldGraphVertex, Node> _closedDic = new();
    private readonly List<Node> _openList = new();
    private Dictionary<WorldGraphVertex, Node?> _openDic = new();
    private int _iterations = 0;
    
    private WorldGraphEntry _worldGraph;
    private WorldGraphVertex _to;
    private Action<List<WorldGraphEdge>> _callback;
    
    private const int HeuristicScale = 1000;
    private const int IndoorWeight = 0;
    private const int MaxIteration = 10000;
    private static readonly HashSet<string> CriterionWhitelist = new(StringComparer.Ordinal)
        { "Ad", "DM", "MI", "Mk", "Oc", "Pc", "QF", "Qo", "Qs", "Sv" };

    public void Initialize()
    {
        _worldGraph = WorldPathFinderService.Instance.GetWorldGraph();
    }
    
    public void Search(WorldGraphVertex from, WorldGraphVertex to, Action<List<WorldGraphEdge>> callback)
    {
        if (from == to)
        {
            callback(new List<WorldGraphEdge>());
            return;
        }
        
        _to = to;
        _callback = callback;

        var toMap = MapRepository.Instance.GetMapPosition(to.MapId);
        if (toMap == null)
        {
            _callback(new List<WorldGraphEdge>());
            return;
        }
        _dest = toMap;
        
        _openList.Clear();
        _closedDic.Clear();
        _openDic = new Dictionary<WorldGraphVertex, Node?>();
        _iterations = 0;
        
        _openList.Add(new Node(from, MapRepository.Instance.GetMapPosition((long)from.MapId)!));
        Compute();
    }

    public void Compute()
    {
        var start = Stopwatch.GetTimestamp();

        while (_openList.Count > 0)
        {
            if (_iterations++ > MaxIteration)
            {           
                _callback(new List<WorldGraphEdge>());
                return;
            }
            
            var current = _openList[0];
            _openList.RemoveAt(0);

            _openDic[current.Vertex] = null;

            if (current.Vertex.MapId == _to.MapId)
            {
                Log.Debug("AStarService.Compute() took {Elapsed}ms to compute a path", (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency * 1000);
                _callback(BuildResultPath(current));
                return;
            }
            
            var edges = _worldGraph.GetOutgoingEdgesFromVertex(current.Vertex);
            var oldLength = _openList.Count;
            var cost = current.Cost + 1;

            foreach (var edge in edges.Where(HasValidTransition))
            {
                var existing = _closedDic.TryGetValue(edge.To, out var node);
                if(existing && node?.Cost < cost)
                    continue;
                
                existing = _openDic.TryGetValue(edge.To, out node);
                if(existing && node?.Cost < cost)
                    continue;
                
                var map = MapRepository.Instance.GetMapPosition((long)edge.To.MapId);
                
                if(map == null)
                    continue;
                
                var manhattanDistance = Math.Abs(map.PosX - _dest.PosX) + Math.Abs(map.PosY - _dest.PosY);
                node = new Node(edge.To, map, cost, cost + HeuristicScale * manhattanDistance + (current.Map.Outdoor && !map.Outdoor ? IndoorWeight : 0), current);
                _openList.Add(node);
                _openDic[edge.To] = node;
            }
            
            _closedDic[current.Vertex] = current;
            
            if(oldLength < _openList.Count)
                _openList.Sort((a, b) =>a == null ? 0: a.Heuristic.CompareTo(b.Heuristic));
        }
        
        _callback([]);
    }

    private bool HasValidTransition(WorldGraphEdge edge)
    {
        var valid = false;
        
        foreach (var transition in edge.Transitions)
        {
            if (string.IsNullOrEmpty(transition.Criterion))
            {
                valid = true;
                break;
            }
            
            if (!transition.Criterion.Contains("&") &&
                !transition.Criterion.Contains("|") &&
                (transition.Criterion.Length < 2 ||
                 !CriterionWhitelist.Contains(transition.Criterion[..2])))
            {
                return false;
            }

            return true;
        }
        
        return valid;
    }
    

    private List<WorldGraphEdge> BuildResultPath(Node node)
    {
        var result = new List<WorldGraphEdge>();

        while (node.Parent != null)
        {
            result.Add(_worldGraph.GetEdge(node.Parent.Vertex, node.Vertex)!);
            node = node.Parent;
        }

        result.Reverse();
        return result;
    }
}


public class Node
{
    public Node? Parent { get; }
    public WorldGraphVertex Vertex { get; }
    public MapPositions Map { get; }
    public int Cost { get; }
    public int Heuristic { get; }

    public Node(WorldGraphVertex vertex, MapPositions map, int cost = 0, int heuristic = 0, Node? parent = null)
    {
        Parent    = parent;
        Cost      = cost;
        Heuristic = heuristic;
        Map       = map;
        Vertex    = vertex;
    }
}