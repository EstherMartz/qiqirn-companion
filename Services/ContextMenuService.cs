using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System;

namespace QiqirnCompanion.Services;

/// <summary>
/// Adds a "Qiqirn Search" entry to the game's native right-click menu. When the
/// menu targets a resolvable item, clicking it opens the item-info window.
/// Resolution covers inventory grids and chat item links; other contexts simply
/// don't get the entry.
/// </summary>
public sealed class ContextMenuService : IDisposable
{
    private const string MenuItemName = "Qiqirn Search";

    private readonly IContextMenu _contextMenu;
    private readonly IDataManager _data;
    private readonly Action<uint, string> _openItem;

    public ContextMenuService(IContextMenu contextMenu, IDataManager data, Action<uint, string> openItem)
    {
        _contextMenu = contextMenu;
        _data        = data;
        _openItem    = openItem;
        _contextMenu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!TryResolveItemId(args, out var itemId)) return;

        var name = ResolveName(itemId);
        if (string.IsNullOrEmpty(name)) return;

        args.AddMenuItem(new MenuItem
        {
            Name        = MenuItemName,
            PrefixChar  = 'Q',
            PrefixColor = 706,
            OnClicked   = _ => _openItem(itemId, name),
        });
    }

    private static unsafe bool TryResolveItemId(IMenuOpenedArgs args, out uint itemId)
    {
        itemId = 0;
        switch (args.Target)
        {
            // Bags, saddlebag, armoury, retainer inventory, etc.
            case MenuTargetInventory { TargetItem: { } gi }:
                itemId = gi.BaseItemId;
                break;

            // Right-clicking an item link printed in chat.
            case MenuTargetDefault when args.AddonName == "ChatLog":
                itemId = NormalizeItemId(AgentChatLog.Instance()->ContextItemId);
                break;
        }
        return itemId != 0;
    }

    // Strip HQ (+1,000,000) / collectable (+500,000) offsets down to the base row id.
    private static uint NormalizeItemId(uint id) =>
        id >= 1_000_000 ? id - 1_000_000 :
        id >= 500_000   ? id - 500_000   :
        id;

    private string ResolveName(uint itemId)
    {
        if (itemId == 0) return "";
        try
        {
            return _data.GetExcelSheet<Item>()?.GetRowOrDefault(itemId)?.Name.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose() => _contextMenu.OnMenuOpened -= OnMenuOpened;
}
