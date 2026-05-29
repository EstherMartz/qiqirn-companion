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
    /// Read all item stacks from the player's bags.
    /// Multiple stacks of the same item are returned as separate entries;
    /// call AggregatedBags to merge them.
    /// </summary>
    public static unsafe List<(int ItemId, int Qty)> ReadBags(bool includeSaddlebag = false)
    {
        var results = new List<(int, int)>();
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
                results.Add(((int)slot->ItemId, (int)slot->Quantity));
            }
        }

        return results;
    }

    /// <summary>
    /// Read bags and merge stacks of the same item into a single entry per itemId.
    /// </summary>
    public static Dictionary<int, int> AggregatedBags(bool includeSaddlebag = false)
    {
        var agg = new Dictionary<int, int>();
        foreach (var (itemId, qty) in ReadBags(includeSaddlebag))
        {
            agg[itemId] = agg.GetValueOrDefault(itemId, 0) + qty;
        }
        return agg;
    }
}
