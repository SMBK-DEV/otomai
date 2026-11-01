using Bubble.Core.Datacenter;
using Bubble.Core.Datacenter.Datacenter.Monster;

namespace BubbleBot.Cli.Repository;

/// <summary>
/// Read-only access to the locally installed Dofus Unity monster datacenter.
/// No third-party API or remote data source is contacted.
/// </summary>
internal static class MonsterRepository
{
    private static readonly Lazy<IReadOnlyDictionary<ushort, Monsters>> MonstersById =
        new(() => DatacenterService.Load<Monsters>().Values
            .GroupBy(monster => monster.Id)
            .ToDictionary(group => group.Key, group => group.First()));

    public static Monsters? Get(ushort id)
    {
        try
        {
            return MonstersById.Value.TryGetValue(id, out var monster) ? monster : null;
        }
        catch (InvalidOperationException)
        {
            // The Unity datacenter has not been initialized by the host.
            return null;
        }
    }

    public static IReadOnlyList<Monsters> Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
            return [];

        try
        {
            return MonstersById.Value.Values
                .Where(monster => DatacenterService.GetText((uint)monster.NameId).Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(monster => DatacenterService.GetText((uint)monster.NameId), StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 50))
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
