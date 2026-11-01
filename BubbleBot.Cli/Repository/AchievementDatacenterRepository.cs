using Bubble.Core.Datacenter;
using Bubble.Core.Datacenter.Datacenter.Quest;

namespace BubbleBot.Cli.Repository;

/// <summary>Read-only achievement templates from the local Dofus Unity datacenter.</summary>
internal static class AchievementDatacenterRepository
{
    private static readonly Lazy<IReadOnlyDictionary<int, Achievements>> AchievementsById =
        new(() => DatacenterService.Load<Achievements>().Values
            .GroupBy(achievement => achievement.Id)
            .ToDictionary(group => group.Key, group => group.First()));

    public static Achievements? Get(int id)
    {
        try { return AchievementsById.Value.TryGetValue(id, out var achievement) ? achievement : null; }
        catch (InvalidOperationException) { return null; }
    }

    public static IReadOnlyList<Achievements> Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0) return [];
        try
        {
            return AchievementsById.Value.Values
                .Where(achievement => DatacenterService.GetText(achievement.NameId)
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(achievement => DatacenterService.GetText(achievement.NameId), StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 50))
                .ToArray();
        }
        catch (InvalidOperationException) { return []; }
    }
}
