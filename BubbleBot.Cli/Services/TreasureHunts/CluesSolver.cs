using System.Globalization;
using System.Text;
using System.Text.Json;
using Bubble.Core.Datacenter;
using Bubble.Core.Datacenter.Datacenter.Quest.TreasureHunt;
using Bubble.Core.Services;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.TreasureHunts.Models;
using Serilog;

namespace BubbleBot.Cli.Services.TreasureHunts;

public class CluesSolver : Singleton<CluesSolver>
{
    private DofusPourLesNoobsFile? _clues;
    private Dictionary<string, List<DofusDbResponseData>> _cache = new();

    public static string RemoveAccents(string str)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            return string.Empty;
        }

        var normalized = str.Normalize(NormalizationForm.FormD);
        StringBuilder stringBuilder = new(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(character);
            }
        }

        return stringBuilder.ToString()
                            .Replace("œ", "oe", StringComparison.OrdinalIgnoreCase)
                            .Normalize(NormalizationForm.FormC)
                            .Trim();
    }

    public async Task Initialize()
    {
        if (File.Exists("Data/clues.json"))
        {
            var cacheFile = await File.ReadAllTextAsync("Data/clues.json");
            _cache = JsonSerializer.Deserialize<Dictionary<string, List<DofusDbResponseData>>>(cacheFile) ?? new();
        }
        
        var cluesInGame = DatacenterService.Load<PointOfInterest>();
        
        var cluesLocal = await File.ReadAllTextAsync("Data/dofuspourlesnoobs_clues.json");
        
        var clues = JsonSerializer.Deserialize<DofusPourLesNoobsFile>(cluesLocal)
                    ?? throw new InvalidDataException("Unable to deserialize local treasure hunt clues.");

        // Only map a localized hint when its normalized name identifies exactly one game POI.
        // Ambiguous or unknown names must never fall back to an unrelated external clue ID.
        var gameCluesByName = cluesInGame.Values
                                         .GroupBy(x => RemoveAccents(x.Name), StringComparer.OrdinalIgnoreCase)
                                         .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        var cluesMapping = new Dictionary<int, int>();
        var unmatchedClueNames = 0;
        var ambiguousClueNames = 0;

        foreach (var clue in clues.Clues)
        {
            var normalizedName = RemoveAccents(clue.HintFr);
            if (!gameCluesByName.TryGetValue(normalizedName, out var matchingGameClues))
            {
                unmatchedClueNames++;
                continue;
            }

            if (matchingGameClues.Count != 1)
            {
                ambiguousClueNames++;
                continue;
            }

            cluesMapping[clue.ClueId] = matchingGameClues[0].Id;
        }

        var droppedMapClueReferences = 0;
        foreach (var map in clues.Maps)
        {
            var mappedClues = new List<int>(map.Clues.Count);
            foreach (var sourceClueId in map.Clues)
            {
                if (cluesMapping.TryGetValue(sourceClueId, out var gameClueId))
                {
                    mappedClues.Add(gameClueId);
                }
                else
                {
                    droppedMapClueReferences++;
                }
            }

            map.Clues = mappedClues.Distinct().ToList();
        }

        Log.Information(
            "Loaded local treasure clues: {Mapped}/{Total} clue names mapped, {Unmatched} unmatched, {Ambiguous} ambiguous, {DroppedReferences} map references excluded",
            cluesMapping.Count,
            clues.Clues.Count,
            unmatchedClueNames,
            ambiguousClueNames,
            droppedMapClueReferences);

        _clues = clues;
    }

    public (int x, int y) SolveClueFromLocal(int clueId, int fromX, int fromY, Direction direction)
    {
        if(_clues == null)
            return (666, 666);
        
        var maxDistance = 10;

        for (var i = 1; i <= maxDistance; i++)
        {
            var (x, y) = (fromX, fromY);
            switch (direction)
            {
                case Direction.North:
                    y -= i;
                    break;
                case Direction.South:
                    y += i;
                    break;
                case Direction.East:
                    x += i;
                    break;
                case Direction.West:
                    x -= i;
                    break;
            }
            
            var mapInfo =  _clues.Maps.FirstOrDefault(z => z.X == x && z.Y == y);
            
            if (mapInfo == null)
                continue;
            
            if (mapInfo.Clues.Contains(clueId))
            {
                return (x, y);
            }
        }

        return (666, 666);
    }

    public (int x, int y) SolveClue(int clueId, int x, int y, int direction)
    {
        try
        {
            var cacheKey = $"{x}_{y}_{direction}";

            if (!_cache.TryGetValue(cacheKey, out var clues))
            {
                return (666, 666);
            }
            
            var clueRelated = clues.FirstOrDefault(z => z.Pois.Any(p => p.Id == clueId));

            if (clueRelated == null)
            {
                return (666, 666);
            }

            return (clueRelated.PosX, clueRelated.PosY);
        }
        catch (Exception e)
        {
            return (666, 666);
        }
    }


    
}