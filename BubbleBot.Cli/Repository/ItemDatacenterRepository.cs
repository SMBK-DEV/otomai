using Bubble.Core.Datacenter;
using Bubble.Core.Datacenter.Datacenter.Items;

namespace BubbleBot.Cli.Repository;

/// <summary>Read-only access to item templates in the installed Dofus Unity datacenter.</summary>
internal static class ItemDatacenterRepository
{
    private static readonly Lazy<IReadOnlyDictionary<ushort, Items>> ItemsById =
        new(() => DatacenterService.Load<Items>().Values
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First()));

    public static Items? Get(ushort id)
    {
        try { return ItemsById.Value.TryGetValue(id, out var item) ? item : null; }
        catch (InvalidOperationException) { return null; }
    }

    public static IReadOnlyList<Items> Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0) return [];
        try
        {
            return ItemsById.Value.Values
                .Where(item => DatacenterService.GetText(item.NameId)
                    .Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => DatacenterService.GetText(item.NameId), StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(limit, 1, 50))
                .ToArray();
        }
        catch (InvalidOperationException) { return []; }
    }
}
