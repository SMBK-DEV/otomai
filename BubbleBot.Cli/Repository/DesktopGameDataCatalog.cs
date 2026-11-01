using Bubble.Core.Datacenter;
using BubbleBot.Cli.Repository;

namespace BubbleBot.Cli;

/// <summary>Read-only, bounded desktop search over locally installed Unity datacenters.</summary>
public static class DesktopGameDataCatalog
{
    public static IReadOnlyList<DesktopGameDataEntry> Search(string category, string query, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        limit = Math.Clamp(limit, 1, 50);
        try
        {
            return category.ToLowerInvariant() switch
            {
                "items" => ItemDatacenterRepository.Search(query, limit)
                    .Select(x => new DesktopGameDataEntry(x.Id, DatacenterService.GetText(unchecked((uint)x.NameId)), null))
                    .ToArray(),
                "monsters" => MonsterRepository.Search(query, limit)
                    .Select(x => new DesktopGameDataEntry(x.Id, DatacenterService.GetText(unchecked((uint)x.NameId)), null))
                    .ToArray(),
                "achievements" => AchievementDatacenterRepository.Search(query, limit)
                    .Select(x => new DesktopGameDataEntry(x.Id, DatacenterService.GetText(unchecked((uint)x.NameId)),
                        AchievementDatacenterRepository.GetObjectives(x.Id).Count))
                    .ToArray(),
                _ => []
            };
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}

public sealed record DesktopGameDataEntry(int Id, string Name, int? ObjectiveCount);
