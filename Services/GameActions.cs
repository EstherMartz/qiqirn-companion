using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace QiqirnCompanion.Services;

/// <summary>
/// Thin wrappers over in-game functionality. All actions are local to the
/// player — nothing is ever sent to a public chat channel.
/// </summary>
public static class GameActions
{
    /// <summary>
    /// Print a clickable item link to the player's OWN chat log only.
    /// (Equivalent to the game's right-click "Link" option, but visible solely
    /// to the user — not broadcast to /say or any channel.)
    /// </summary>
    public static void LinkItemInChat(IChatGui chat, uint itemId, string name, bool hq)
    {
        var payload = new SeStringBuilder()
            .AddItemLink(itemId, hq)
            .Build();
        chat.Print(payload);
    }

    /// <summary>
    /// Open the Crafting Log to recipes that produce / use this item
    /// (native "Search Recipes Using This Material" / "Open Recipe Log").
    /// </summary>
    public static unsafe void OpenRecipeLog(uint itemId)
    {
        AgentRecipeNote.Instance()->SearchRecipeByItemId(itemId);
    }

    /// <summary>
    /// Open the in-game Gathering Log to this item (native "Gathering Log"),
    /// which shows the zones and nodes where it can be collected.
    /// </summary>
    public static unsafe void OpenGatheringLog(uint itemId)
    {
        // Native signature takes a ushort; gatherable item row ids fit (HQ/collectable
        // offsets that exceed ushort are stripped before this is ever called).
        AgentGatheringNote.Instance()->OpenGatherableByItemId((ushort)itemId);
    }

    /// <summary>
    /// Open the in-game item search list for this item (native "Search for Item").
    /// </summary>
    public static unsafe void SearchForItem(uint itemId)
    {
        ItemFinderModule.Instance()->SearchForItem(itemId);
    }
}
