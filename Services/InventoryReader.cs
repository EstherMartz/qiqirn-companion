using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;

namespace QiqirnCompanion.Services;

public static class InventoryReader
{
    // The four main character bags.
    private static readonly InventoryType[] MainBags =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.Crystals,   // crystals/shards — required by every recipe
    ];

    // Chocobo saddlebag (optional).
    private static readonly InventoryType[] SaddleBags =
    [
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
    ];

    /// <summary>
    /// Read all item stacks from the player's bags, including the HQ flag.
    /// Multiple stacks of the same item are returned as separate entries.
    /// </summary>
    public static unsafe List<(int ItemId, int Qty, bool Hq)> ReadBags(bool includeSaddlebag = false)
    {
        var results = new List<(int, int, bool)>();
        var manager = InventoryManager.Instance();
        if (manager == null) return results;

        var bagTypes = includeSaddlebag
            ? [.. MainBags, .. SaddleBags]
            : MainBags;

        foreach (var bagType in bagTypes)
        {
            var container = manager->GetInventoryContainer(bagType);
            if (container == null) continue;

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;
                var hq = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
                results.Add(((int)slot->ItemId, (int)slot->Quantity, hq));
            }
        }

        return results;
    }

    /// <summary>
    /// Read bags and merge stacks of the same item into a single entry per itemId
    /// (HQ/NQ pooled). Used by the Crafting tab.
    /// </summary>
    public static Dictionary<int, int> AggregatedBags(bool includeSaddlebag = false)
    {
        var agg = new Dictionary<int, int>();
        foreach (var (itemId, qty, _) in ReadBags(includeSaddlebag))
        {
            agg[itemId] = agg.GetValueOrDefault(itemId, 0) + qty;
        }
        return agg;
    }

    /// <summary>
    /// Read bags aggregated by (itemId, hq) — keeps HQ and NQ stacks separate so
    /// the cleanup classifier can price each on its correct market tier.
    /// </summary>
    public static List<(int Id, int Qty, bool Hq)> AggregatedForCleanup(bool includeSaddlebag = false)
    {
        var agg = new Dictionary<(int, bool), int>();
        foreach (var (itemId, qty, hq) in ReadBags(includeSaddlebag))
        {
            var key = (itemId, hq);
            agg[key] = agg.GetValueOrDefault(key, 0) + qty;
        }
        var list = new List<(int, int, bool)>();
        foreach (var (key, qty) in agg) list.Add((key.Item1, qty, key.Item2));
        return list;
    }
}
