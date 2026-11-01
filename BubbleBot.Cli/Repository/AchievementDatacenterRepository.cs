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

    private static readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<AchievementObjectives>>> ObjectivesByAchievementId =
        new(() => DatacenterService.Load<AchievementObjectives>().Values
            .GroupBy(objective => objective.AchievementId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AchievementObjectives>)group
                    .OrderBy(objective => objective.Order)
                    .ToArray()));

    public static Achievements? Get(int id)
    {
        try { return AchievementsById.Value.TryGetValue(id, out var achievement) ? achievement : null; }
        catch (InvalidOperationException) { return null; }
    }

    public static IReadOnlyList<AchievementObjectives> GetObjectives(int achievementId)
    {
        try
        {
            return ObjectivesByAchievementId.Value.TryGetValue(achievementId, out var objectives)
                ? objectives
                : [];
        }
        catch (InvalidOperationException) { return []; }
    }

    public static IReadOnlyList<Achievements> Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0) return [];
        try
        {
            return AchievementsById.Value.Values
                .Where(achievement => DatacenterService.GetText(unchecked((uint)achievement.NameId))
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(achievement => DatacenterService.GetText(unchecked((uint)achievement.NameId)), StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 50))
                .ToArray();
        }
        catch (InvalidOperationException) { return []; }
    }
}
