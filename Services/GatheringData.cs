using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace QiqirnCompanion.Services;

/// <summary>
/// Local lookup for whether an item can be gathered (mining/botany). Built once
/// from Lumina's GatheringItem sheet so both the native and in-plugin right-click
/// menus can decide whether to offer "Gather Item" without any network call.
/// Fishing is intentionally not covered (it uses a separate Fishing Log).
/// </summary>
public static class GatheringData
{
    private static readonly HashSet<uint> Gatherable = new();

    public static void Initialize(IDataManager data)
    {
        try
        {
            var sheet = data.GetExcelSheet<GatheringItem>();
            if (sheet is null) return;
            foreach (var row in sheet)
            {
                var itemId = (uint)row.Item.RowId;
                if (itemId != 0) Gatherable.Add(itemId);
            }
        }
        catch
        {
            // Leave the set empty; IsGatherable then returns false and the
            // "Gather Item" entries simply won't appear.
        }
    }

    public static bool IsGatherable(uint itemId) => Gatherable.Contains(itemId);
}
