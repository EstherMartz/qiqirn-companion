using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using System;

namespace QiqirnCompanion.Services;

/// <summary>
/// Unified item interactions shared by every table in the plugin. Call
/// <see cref="HandleRow"/> immediately after rendering an item's hoverable
/// widget (e.g. a Selectable):
///   • left-click       → copy the item name to the clipboard
///   • double-click     → link the item in the player's own chat log
///   • right-click      → context menu (external sites + native game actions)
/// </summary>
public static class ItemInteractions
{
    private static IChatGui? _chat;

    public static void Initialize(IChatGui chat) => _chat = chat;

    public static void HandleRow(uint itemId, string name, bool hq = false)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Left-click: copy  •  Double-click: link in chat  •  Right-click: more");

            // Double-click wins over single-click on the second press, so a
            // double-click links exactly once without an extra copy.
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (_chat != null) GameActions.LinkItemInChat(_chat, itemId, name, hq);
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(name);
            }
        }

        if (ImGui.BeginPopupContextItem($"##ictx{itemId}"))
        {
            DrawContextMenu(itemId, name);
            ImGui.EndPopup();
        }
    }

    private static void DrawContextMenu(uint itemId, string name)
    {
        ImGui.TextDisabled(name);
        ImGui.Separator();

        if (ImGui.MenuItem("View on Qiqirn.tools"))
            Util.OpenLink($"https://qiqirn.tools/item/{itemId}");
        if (ImGui.MenuItem("View on GarlandTools"))
            Util.OpenLink($"https://www.garlandtools.org/db/#item/{itemId}");
        if (ImGui.MenuItem("View on Universalis"))
            Util.OpenLink($"https://universalis.app/market/{itemId}");
        if (ImGui.MenuItem("View on Gamer Escape"))
            Util.OpenLink($"https://ffxiv.gamerescape.com/wiki/{Uri.EscapeDataString(name.Replace(' ', '_'))}");

        ImGui.Separator();

        if (ImGui.MenuItem("Search for Item"))
            GameActions.SearchForItem(itemId);
        if (ImGui.MenuItem("Search Recipes / Open Recipe Log"))
            GameActions.OpenRecipeLog(itemId);
    }
}
