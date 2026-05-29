using FFXIVClientStructs.FFXIV.Client.Game;

namespace QiqirnCompanion.Services;

/// <summary>Reads the player's current gil from the game.</summary>
public static class GilReader
{
    /// <summary>Current gil, or null if unavailable (e.g. not logged in).</summary>
    public static unsafe long? CurrentGil()
    {
        var inv = InventoryManager.Instance();
        if (inv == null) return null;
        return inv->GetGil();
    }
}
