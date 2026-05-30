# Smart Item Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `/qiqirn <item>` search (jumping straight to a dedicated item-info window on an exact match) and add a "Qiqirn Search" entry to the game's native right-click menu that opens that same window directly.

**Architecture:** Extract the single-item market/sources view out of `SearchWindow`'s popup modal into a new non-modal `ItemInfoWindow`. Three entry points feed it: clicking a search row, an exact-match from the command, and a native context-menu click. The command parses args in `Plugin.OnCommand`; the native menu is handled by a new `ContextMenuService` wrapping Dalamud's `IContextMenu`.

**Tech Stack:** C# / .NET 10, Dalamud API level 15 (`IContextMenu`, `Dalamud.Game.Gui.ContextMenu`), FFXIVClientStructs (`AgentChatLog`), Lumina (`Item` sheet via `IDataManager`), Dalamud.Bindings.ImGui.

---

## Testing note (read first)

This plugin has **no unit-test harness** (it references the game's runtime DLLs; there is no test project, and the existing codebase has zero tests). Per writing-plans "follow established patterns", we do **not** add one. The verification loop for every task is:

1. **Compile gate:** `dotnet build -c Debug` from the repo root must succeed with no errors. This is the primary automated check — it catches wrong type/member/field names immediately (full IntelliSense/source for Dalamud + FFXIVClientStructs is available locally via the `DalamudLibPath` references in the csproj).
2. **Manual in-game checks** where noted (the Debug build auto-deploys to `%AppData%\XIVLauncher\devPlugins\QiqirnCompanion` via the existing `DeployToDevPlugins` MSBuild target; reload via `/xldev` → Reload).

Each task ends with the compile gate + a commit. In-game checks are listed once at the end of the relevant tasks.

---

## File structure

- **Create** `Windows/ItemInfoWindow.cs` — owns the single-item market/sources view (moved out of `SearchWindow`) as a non-modal window opened by item id + name.
- **Create** `Services/ContextMenuService.cs` — subscribes to `IContextMenu.OnMenuOpened`, resolves the item id, adds the "Qiqirn Search" entry, opens `ItemInfoWindow` on click.
- **Modify** `Windows/SearchWindow.cs` — delegate row clicks to `ItemInfoWindow`; add `RunQuery` + exact-match jump; remove the old modal + item-data drawing helpers.
- **Modify** `Plugin.cs` — construct/register `ItemInfoWindow`; inject `IContextMenu`; construct/dispose `ContextMenuService`; parse command args.
- **Modify** `QiqirnCompanion.json` — changelog + version bump.

---

## Task 1: Create `ItemInfoWindow`

Move the item-data view out of `SearchWindow` into its own non-modal window. This is a pure extraction — the drawing code is lifted verbatim from `SearchWindow` (which still references these methods until Task 2, so the build stays green by **adding** the new file first, then removing the originals in Task 2).

**Files:**
- Create: `Windows/ItemInfoWindow.cs`

- [ ] **Step 1: Create the window file**

Create `Windows/ItemInfoWindow.cs` with this exact content:

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using QiqirnCompanion.Services;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Non-modal window showing market + sources data for a single item. Opened by
/// item id from three places: a Search row click, an exact-match from the
/// <c>/qiqirn &lt;item&gt;</c> command, and the native "Qiqirn Search" context menu.
/// </summary>
public class ItemInfoWindow : Window, IDisposable
{
    private readonly ApiClient _api;

    private uint   _itemId;
    private string _itemName = "";
    private ItemSourcesResponse? _sources;
    private bool   _isLoading;
    private string? _error;
    private bool   _focusNext;

    public ItemInfoWindow(ApiClient api) : base("Item Info##qiqirn-iteminfo")
    {
        _api = api;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(1000, 900),
        };
    }

    /// <summary>Open (or refocus) the window on the given item and load its data.</summary>
    public void Show(uint itemId, string name)
    {
        _itemId    = itemId;
        _itemName  = name;
        _sources   = null;
        _error     = null;
        _isLoading = true;
        IsOpen     = true;
        _focusNext = true;
        _ = LoadSources(itemId);
    }

    // Focus the window the first frame after Show(), on the draw thread.
    public override void PreDraw()
    {
        if (_focusNext)
        {
            ImGui.SetNextWindowFocus();
            _focusNext = false;
        }
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.4f, 1), _itemName);
        ImGui.Separator();

        if (_error != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
            ImGui.TextWrapped($"Error: {_error}");
            ImGui.PopStyleColor();
            return;
        }

        if (_isLoading)
        {
            ImGui.Text("Loading...");
            return;
        }

        if (_sources != null)
            DrawSourcesList(_sources);
    }

    private async Task LoadSources(uint itemId)
    {
        try
        {
            var result = await _api.GetItemSourcesAsync((int)itemId);
            // Guard against an out-of-order response when Show() was called again
            // with a different item while this request was in flight.
            if (_itemId != itemId) return;
            _sources = result;
            _error   = null;
        }
        catch (Exception ex)
        {
            if (_itemId != itemId) return;
            _error   = ex.Message;
            _sources = null;
        }
        finally
        {
            if (_itemId == itemId) _isLoading = false;
        }
    }

    private static string FormatGil(long v) =>
        v >= 1_000_000 ? $"{v / 1_000_000.0:F1}M"
        : v >= 1_000   ? $"{v / 1_000.0:F0}k"
        : v.ToString();

    private void DrawMarketSummary(MarketSummary? market)
    {
        if (market == null) return;
        ImGui.TextColored(new Vector4(0.5f, 0.85f, 1f, 1), "Market");
        ImGui.Indent();
        ImGui.TextUnformatted($"Sales/day: {(market.Velocity > 0 ? market.Velocity.ToString("F1") : "—")}   "
            + $"Listings: {market.ListingCount}");
        if (market.CheapestWorld != null && market.CheapestPrice.HasValue)
            ImGui.TextUnformatted($"Cheapest: {market.CheapestWorld} @ {FormatGil(market.CheapestPrice.Value)} gil");
        ImGui.Unindent();
        ImGui.Separator();
    }

    private void DrawSourcesList(ItemSourcesResponse sources)
    {
        DrawMarketSummary(sources.Market);

        if (sources.Sources.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No sources available for this item");
            return;
        }

        foreach (var source in sources.Sources)
        {
            switch (source)
            {
                case RecipeSource recipe:            DrawRecipeSource(recipe);            break;
                case VendorSource vendor:            DrawVendorSource(vendor);            break;
                case GatheringSource gathering:      DrawGatheringSource(gathering);      break;
                case SpecialShopSource specialShop:  DrawSpecialShopSource(specialShop);  break;
                case CompanyCraftSource companyCraft: DrawCompanyCraftSource(companyCraft); break;
            }
            ImGui.Spacing();
        }
    }

    private void DrawRecipeSource(RecipeSource recipe)
    {
        ImGui.TextColored(new Vector4(0.2f, 0.8f, 1, 1), $"📖 {recipe.JobName} (Lv. {recipe.Level})");
        ImGui.Indent();
        ImGui.Text($"Yield: {recipe.OutputQty}");
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), "Ingredients:");
        foreach (var ing in recipe.Ingredients)
            ImGui.BulletText($"{ing.ItemName} x{ing.Qty}");
        ImGui.Unindent();
    }

    private void DrawVendorSource(VendorSource vendor)
    {
        ImGui.TextColored(new Vector4(1, 0.8f, 0.2f, 1), $"🏪 NPC Vendor");
        ImGui.Indent();
        ImGui.Text($"Price: {vendor.Price:N0} gil");
        ImGui.Unindent();
    }

    private void DrawGatheringSource(GatheringSource gathering)
    {
        var timedLabel = gathering.Timed ? " (Timed)" : "";
        ImGui.TextColored(new Vector4(0.2f, 1, 0.2f, 1), $"⛏️ Gathering (Lv. {gathering.Level}){timedLabel}");
    }

    private void DrawSpecialShopSource(SpecialShopSource specialShop)
    {
        ImGui.TextColored(new Vector4(1, 0.5f, 0.8f, 1), $"⭐ Special Shop");
        ImGui.Indent();
        ImGui.Text($"Cost: {specialShop.Cost} {specialShop.Currency}");
        ImGui.Unindent();
    }

    private void DrawCompanyCraftSource(CompanyCraftSource companyCraft)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0.2f, 1), $"🏢 {companyCraft.CraftName}");
        ImGui.Indent();
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), "Materials:");
        foreach (var ing in companyCraft.Ingredients)
            ImGui.BulletText($"{ing.ItemName} x{ing.Qty}");
        ImGui.Unindent();
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Compile gate**

Run: `dotnet build -c Debug`
Expected: Build succeeded, 0 errors. (`ItemInfoWindow` is not yet wired anywhere — that's fine; it must compile on its own. The drawing helpers still also exist in `SearchWindow`; the duplicate private methods live in different classes so there is no conflict.)

- [ ] **Step 3: Commit**

```bash
git add Windows/ItemInfoWindow.cs
git commit -m "feat: add ItemInfoWindow (single-item market/sources view)"
```

---

## Task 2: Wire `ItemInfoWindow` into `SearchWindow` + `Plugin`, remove the old modal

Now make the search row open the new window, add the `RunQuery` + exact-match entry point, and delete the now-duplicated modal/drawing code from `SearchWindow`.

**Files:**
- Modify: `Windows/SearchWindow.cs`
- Modify: `Plugin.cs:49` (construct `ItemInfoWindow`, pass to `SearchWindow`, register window)

- [ ] **Step 1: Give `SearchWindow` an `ItemInfoWindow` dependency**

In `Windows/SearchWindow.cs`, change the constructor and the field block. Replace the field declarations (current lines 13-29) and constructor (current lines 31-40) so they read:

```csharp
    private readonly ApiClient _api;
    private readonly ItemInfoWindow _itemInfo;

    // Search state
    private string _searchQuery = "";
    private List<ItemSearchResult> _searchResults = new();
    private bool _isSearching = false;
    private string? _searchError = null;
    private int _currentPage = 1;
    private int _totalResults = 0;
    private const int PageSize = 20;

    // Set by RunQuery; consumed once when the next search completes, to jump
    // straight to ItemInfoWindow on an exact name match.
    private string? _pendingExactQuery = null;

    public SearchWindow(ApiClient api, ItemInfoWindow itemInfo) : base("Item Search")
    {
        _api = api;
        _itemInfo = itemInfo;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(1200, 800),
        };
    }
```

(This removes the `_selectedItem`, `_selectedSources`, `_isLoadingSources`, `_sourcesError`, `_sourcesModalOpen` fields — they belong to the deleted modal.)

- [ ] **Step 2: Drop the modal call and fix the row-selected highlight in `DrawContent`/`DrawResults`**

In `DrawContent()` remove the `DrawSourcesModal();` line (currently line 70).

In `DrawResults()`, the Selectable currently passes `_selectedItem?.Id == item.Id` as its "selected" arg (current line 122). Replace that whole `if (ImGui.Selectable(...))` call with:

```csharp
                if (ImGui.Selectable(item.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                {
                    SelectItem(item);
                }
```

- [ ] **Step 3: Replace `SelectItem` and add `RunQuery`**

Replace the entire `SelectItem` method (current lines 358-367) with:

```csharp
    private void SelectItem(ItemSearchResult item) => _itemInfo.Show((uint)item.Id, item.Name);

    /// <summary>
    /// Run a search from outside the window (the <c>/qiqirn &lt;item&gt;</c> command).
    /// On completion, an exact name match jumps straight to <see cref="ItemInfoWindow"/>.
    /// </summary>
    public void RunQuery(string query)
    {
        _searchQuery = query;
        _currentPage = 1;
        if (query.Length >= 2)
        {
            _pendingExactQuery = query;
            _ = PerformSearch();
        }
        else
        {
            _pendingExactQuery = null;
            _searchResults.Clear();
            _totalResults = 0;
        }
    }
```

- [ ] **Step 4: Add the exact-match jump in `PerformSearch`**

In `PerformSearch` (current lines 369-395), inside the `if (response != null)` block, after `_totalResults = response.Total;`, add the exact-match check. The block becomes:

```csharp
            var response = await _api.SearchItemsAsync(_searchQuery, _currentPage, PageSize);
            if (response != null)
            {
                _searchResults = response.Items;
                _totalResults = response.Total;

                if (_pendingExactQuery is { } exact)
                {
                    _pendingExactQuery = null;
                    var matches = _searchResults.FindAll(r =>
                        string.Equals(r.Name.Trim(), exact, StringComparison.OrdinalIgnoreCase));
                    if (matches.Count == 1)
                        _itemInfo.Show((uint)matches[0].Id, matches[0].Name);
                }
            }
```

- [ ] **Step 5: Delete the moved item-data code from `SearchWindow`**

Delete these now-duplicated members entirely from `SearchWindow.cs` (they live in `ItemInfoWindow` now): `DrawSourcesModal`, `FormatGil`, `DrawMarketSummary`, `DrawSourcesList`, `DrawRecipeSource`, `DrawVendorSource`, `DrawGatheringSource`, `DrawSpecialShopSource`, `DrawCompanyCraftSource`, and `LoadItemSources` (current lines 222-356 and 397-413). After deletion, `SearchWindow` no longer references `ItemSourcesResponse`, `MarketSummary`, or the `*Source` types — that's expected.

- [ ] **Step 6: Construct and register `ItemInfoWindow` in `Plugin`**

In `Plugin.cs`, add the field (next to the other window fields, near line 23):

```csharp
    private readonly ItemInfoWindow _itemInfoWindow;
```

In the constructor's Windows region, change the `_searchWindow` construction and add the info window + registration. Replace current lines 49 and 57 area so the Windows block reads:

```csharp
        // Windows
        _itemInfoWindow = new ItemInfoWindow(_api);
        _searchWindow  = new SearchWindow(_api, _itemInfoWindow);
        _tradingWindow = new TradingWindow(_api);
        _plannerWindow = new PlannerWindow(Config, _salesTracker);
        _cleanupWindow = new CleanupWindow(_api);
        _mainWindow    = new MainWindow(Config, _api, playerState, _searchWindow, _tradingWindow, _plannerWindow, _cleanupWindow);
        _configWindow  = new ConfigWindow(Config);
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);
        _windowSystem.AddWindow(_searchWindow);
        _windowSystem.AddWindow(_itemInfoWindow);
        _windowSystem.AddWindow(_tradingWindow);
        _windowSystem.AddWindow(_plannerWindow);
        _windowSystem.AddWindow(_cleanupWindow);
```

- [ ] **Step 7: Compile gate**

Run: `dotnet build -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Windows/SearchWindow.cs Plugin.cs
git commit -m "feat: search rows open ItemInfoWindow; add RunQuery + exact-match jump"
```

---

## Task 3: `/qiqirn <item>` command parsing

**Files:**
- Modify: `Plugin.cs:63-74` (command registration + `OnCommand`)

- [ ] **Step 1: Update the command help text**

In `Plugin.cs`, change the `AddHandler` `HelpMessage` (current line 65) to:

```csharp
            HelpMessage = "/qiqirn — open the window.  /qiqirn <item> — search for an item.",
```

- [ ] **Step 2: Parse args in `OnCommand`**

Replace the `OnCommand` method (current line 74) with:

```csharp
    private void OnCommand(string command, string args)
    {
        var query = args.Trim();
        if (query.Length == 0)
        {
            ToggleMain();
            return;
        }

        // Open the standalone search window and run the query. On an exact name
        // match, RunQuery's completion path jumps straight to the info window.
        _searchWindow.IsOpen = true;
        _searchWindow.RunQuery(query);
    }
```

- [ ] **Step 3: Compile gate**

Run: `dotnet build -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Plugin.cs
git commit -m "feat: /qiqirn <item> searches; exact match opens item info"
```

- [ ] **Step 5: Manual in-game check** (after `/xldev` → Reload)

- `/qiqirn` → main window toggles (unchanged).
- `/qiqirn iron` → standalone Search window opens with the results list.
- `/qiqirn iron ingot` → Item Info window opens directly on Iron Ingot (exact match).
- `/qiqirn z` (1 char) → window opens, shows the "enter at least 2 characters" hint, no crash.

---

## Task 4: Native context-menu entry "Qiqirn Search"

Add the entry to the game's right-click menu for **inventory items** and **chat item links** (verified, stable resolution). Other contexts resolve to no item → no entry (graceful, matches the approved "everywhere it resolves" behavior).

**Files:**
- Create: `Services/ContextMenuService.cs`
- Modify: `Plugin.cs` (inject `IContextMenu`, construct/dispose the service)

- [ ] **Step 1: Create the service**

Create `Services/ContextMenuService.cs` with this exact content:

```csharp
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
```

- [ ] **Step 2: Inject `IContextMenu` and construct the service in `Plugin`**

In `Plugin.cs`, add the field near the services (next to `_salesTracker`, ~line 27):

```csharp
    private readonly ContextMenuService _contextMenuService;
```

Add `IContextMenu contextMenu` to the constructor parameter list (after `IDataManager dataManager`):

```csharp
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager         commandManager,
        IPlayerState            playerState,
        IChatGui                chatGui,
        IDataManager            dataManager,
        IContextMenu            contextMenu)
```

In the Services region (after `_salesTracker = ...`, ~line 46), construct the service. It must be created **after** `_itemInfoWindow` exists (Task 2 added that to the Windows region below — so place this line at the end of the constructor body, just before the `UiBuilder` hookups, where `_itemInfoWindow` is already assigned):

```csharp
        // Native game right-click menu → open item info.
        _contextMenuService = new ContextMenuService(contextMenu, dataManager, _itemInfoWindow.Show);
```

(Concretely: put it right after `_configWindow = new ConfigWindow(Config);` and the `_windowSystem.AddWindow(...)` calls, before the `// Slash command` block.)

- [ ] **Step 3: Dispose the service**

In `Dispose()` (current lines 79-89), add a line to tear down the menu subscription. After `_salesTracker.Dispose();` add:

```csharp
        _contextMenuService.Dispose();
```

- [ ] **Step 4: Compile gate**

Run: `dotnet build -c Debug`
Expected: Build succeeded, 0 errors. (If `gi.BaseItemId`, `AgentChatLog.ContextItemId`, or `PrefixColor` mismatch the locally-referenced SDK, the compiler names the exact member — correct against the local FFXIVClientStructs / Dalamud source and rebuild.)

- [ ] **Step 5: Commit**

```bash
git add Services/ContextMenuService.cs Plugin.cs
git commit -m "feat: native 'Qiqirn Search' right-click entry for inventory + chat items"
```

- [ ] **Step 6: Manual in-game check** (after `/xldev` → Reload)

- Right-click an item in your bags → menu shows "Qiqirn Search" with a `Q` prefix → click → Item Info window opens on that item.
- Link an item into your own chat (or find an existing item link), right-click it → "Qiqirn Search" → Item Info opens.
- Right-click a **player** (not an item) → no "Qiqirn Search" entry appears.
- Right-click an empty inventory slot → no entry, no crash.

---

## Task 5: Manifest changelog + release build + full verification

**Files:**
- Modify: `QiqirnCompanion.json`

- [ ] **Step 1: Update the manifest**

In `QiqirnCompanion.json`, set the changelog and bump the version (new feature → minor bump, matching the repo's `feat → minor` history). Change these two lines:

```json
  "Changelog": "Smart search: /qiqirn <item> opens results and jumps to item info on an exact match; new 'Qiqirn Search' entry on the game's right-click menu (inventory items + chat links) opens item info directly.",
```
```json
  "AssemblyVersion": "1.3.0.0",
```

(Note: the actual published version is governed by the release tag/CI per `project_plugin_distribution`; this keeps the in-repo manifest consistent. Do not run the release here.)

- [ ] **Step 2: Full Release build (packaging gate)**

Run: `dotnet build -c Release`
Expected: Build succeeded; `latest.zip` produced (the `PackagePlugin` target runs on Release) under the output path, containing `QiqirnCompanion.dll` + `QiqirnCompanion.json`.

- [ ] **Step 3: Commit**

```bash
git add QiqirnCompanion.json
git commit -m "chore: changelog + version bump for smart item search"
```

- [ ] **Step 4: Final manual regression pass** (after `/xldev` → Reload)

Run the full matrix once more, confirming no regressions to the existing Search tab:
- Main window → Search tab still works (type in the box → results; click a row → Item Info window opens instead of the old modal).
- `/qiqirn`, `/qiqirn iron`, `/qiqirn iron ingot` behave per Task 3.
- Inventory + chat-link right-click → "Qiqirn Search" per Task 4.
- Opening Item Info for item A, then quickly for item B, shows B's data (no stale-response flicker).

---

## Out of scope / fast-follow

- **Market board, recipe-tree/crafting-log, gathering-log right-click contexts.** Each needs its own verified agent field (e.g. the market board and `AgentRecipeNote` result item). They resolve to no entry today (graceful). Adding one is a single `case` in `TryResolveItemId` once the field is confirmed against the local FFXIVClientStructs — a clean extension point.
- The plugin's own in-table ImGui right-click menu (`Services/ItemInteractions.cs`) is unchanged.
- No new API endpoints; reuses `SearchItemsAsync` and `GetItemSourcesAsync`.
