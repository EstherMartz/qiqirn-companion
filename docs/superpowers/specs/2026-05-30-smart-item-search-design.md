# Smart item search: `/qiqirn <item>` command + native context-menu entry

**Date:** 2026-05-30
**Repo:** qiqirn-companion (Dalamud plugin)
**Status:** Approved design — ready for implementation plan

## Goal

Make item lookup feel native to the game:

1. `/qiqirn <item name>` opens the search window pre-filled with results. On an
   exact name match, jump straight to a dedicated item-data window.
2. Add a **"Qiqirn Search"** entry to the game's **native** right-click context
   menu (inventory, chat item links, market board, crafting log, …). Because the
   item ID is already known there, it opens the item-data window directly without
   searching.

This does **not** touch the plugin's own in-table ImGui right-click menu in
`Services/ItemInteractions.cs`; that stays as-is. This feature adds the *game's*
native menu entry only.

## Current state (relevant facts)

- `Plugin.OnCommand` ignores `args` and always toggles `MainWindow`
  (`Plugin.cs:74`).
- `SearchWindow` is a real `Window` but is only rendered as an embedded tab
  inside `MainWindow` via `DrawContent()`. It is a **single shared instance**
  (`Plugin.cs:49`, used by both `MainWindow` and `_windowSystem`), so its query
  and results are identical whether shown as the Search tab or as its own
  standalone window.
- Per-item data ("info") is currently a **modal popup** (`BeginPopupModal`,
  `Sources: {name}`) that lives inside `SearchWindow` and is opened by
  `SelectItem` → `LoadItemSources` → `ApiClient.GetItemSourcesAsync`.
- The native menu shown in the user's screenshot is the game's item context menu
  ("Search for Item", "Link", "Search Recipes Using This Material", "Copy Item
  Name", "Search in Market Board", "Add to Craft List"). Integrating with it uses
  Dalamud's `IContextMenu` service — separate from the plugin's own ImGui menus.
- Dalamud API level 15; Lumina is referenced and available for local item
  name ↔ ID resolution. `GameActions` already does native game calls
  (`SearchForItem`, `OpenRecipeLog`, `LinkItemInChat`).

## Components

### 1. `Windows/ItemInfoWindow.cs` (new)

A dedicated, **non-modal** `Window` added to the `WindowSystem`. Single
responsibility: given an item ID + name, show its market summary + sources.

- **Public API:** `void Show(uint itemId, string name)` — stores the target,
  sets `IsOpen = true`, calls `BringToFront`/focus, and starts an async
  `LoadItemSources(itemId)` using the existing `ApiClient.GetItemSourcesAsync`.
- **Body:** reuses the item-data drawing helpers currently in `SearchWindow`
  (`DrawMarketSummary`, `DrawSourcesList`, `DrawRecipeSource`, `DrawVendorSource`,
  `DrawGatheringSource`, `DrawSpecialShopSource`, `DrawCompanyCraftSource`,
  `FormatGil`). These **move** from `SearchWindow` into `ItemInfoWindow`.
- **State:** loading flag, error string, current item id/name, loaded
  `ItemSourcesResponse?`. Mirrors the fields that backed the old modal.
- Title shows the item name. Window is closable/movable; reopening with a new
  item replaces the contents.

### 2. `Services/ContextMenuService.cs` (new)

Parallels `ItemInteractions`. Owns the native context-menu integration.

- **Construction:** receives `IContextMenu` and a callback/reference to open the
  info window, e.g. `Action<uint, string> openItem`. Subscribes
  `IContextMenu.OnMenuOpened += OnMenuOpened` in the constructor; unsubscribes in
  `Dispose`.
- **`OnMenuOpened`:** resolve the target item ID (see below). If resolved and the
  ID is a real item (> 0), add a `MenuItem { Name = "Qiqirn Search",
  PrefixChar = 'Q' }` whose `OnClicked` calls `openItem(itemId, name)`.
- **Item-ID resolution:**
  - `MenuTargetInventory` → take the item directly from the target's item
    (`ItemId` / base item ID).
  - Default item menu (chat links, market board, crafting log, etc.) → read the
    hovered/active item ID from the context agent (the same source the game's own
    "Search for Item" entry uses). Exact `FFXIVClientStructs` field names are
    pinned during implementation against the local Dalamud.dll/FFXIVClientStructs.
  - If no valid item ID resolves, **do not add** the entry (no crash, no empty
    window). This satisfies the "everywhere it resolves" scope.
- **Name lookup:** resolve display name from the item ID via Lumina's Item sheet
  (so the info window title is correct even when the menu didn't carry a name).

### 3. `Windows/SearchWindow.cs` (modified)

- The item-data drawing helpers and the `Sources:` modal are removed; that
  responsibility moves to `ItemInfoWindow`.
- `SelectItem(item)` now calls `_itemInfo.Show(item.Id, item.Name)` instead of
  opening the modal. (Search window receives the `ItemInfoWindow` reference via
  its constructor.)
- New `void RunQuery(string query)` — sets `_searchQuery`, resets to page 1, and
  triggers `PerformSearch()`. Used by the command path.
- The search-completion path performs the **exact-match check** (see command
  flow). Implemented where results are assigned in `PerformSearch`, guarded so it
  only fires for command-initiated queries (e.g. a `pendingExactQuery` field set
  by `RunQuery` and cleared after the check), not for every keystroke in the box.

### 4. `Plugin.cs` (modified)

- Inject `IContextMenu` into the constructor.
- Construct `ItemInfoWindow`, add it to `_windowSystem`, and pass it to
  `SearchWindow`.
- Construct `ContextMenuService(contextMenu, (id, name) => _itemInfoWindow.Show(id, name))`.
- `OnCommand(command, args)`:
  - `args` blank/whitespace → `ToggleMain()` (unchanged behavior).
  - non-blank → `_searchWindow.RunQuery(args.Trim()); _searchWindow.IsOpen = true;`
    The exact-match jump is handled inside the search-completion path.
- Update the command `HelpMessage` to describe both forms.
- `Dispose` disposes/removes the new window and disposes `ContextMenuService`
  (which unsubscribes the menu handler).

### 5. `QiqirnCompanion.json` (modified)

Changelog entry for the release; assembly version bump follows the existing
release convention (handled at release time, not in this change unless the
implementation plan says otherwise).

## Data flow

```
/qiqirn <name> ─► SearchWindow.RunQuery(name) ─► API search ─► exact match? ─► ItemInfoWindow.Show(id, name)
                                                       └─ no ─► results list (standalone SearchWindow open)

native right-click ─► ContextMenuService resolves itemId ─► ItemInfoWindow.Show(id, name)   (no search)

search row click ─► ItemInfoWindow.Show(id, name)   (replaces today's modal)
```

## Exact-match rule

After a command-initiated search returns, if exactly one result's `Name` equals
the query (case-insensitive, trimmed) → `ItemInfoWindow.Show(match.Id,
match.Name)`. Zero matches, or multiple identical names (rare), fall back to the
results list with the standalone window open.

## Error handling / edge cases

- Command query < 2 chars after trim → reuse the existing "enter ≥2 characters"
  guard; still open the window so the user sees why nothing searched.
- API failure on the command path → existing `_searchError` renders in the
  now-visible window; no exact-match jump.
- Context menu where the agent yields no valid item ID → entry omitted.
- Exact-match check is case-insensitive + trimmed; ties fall back to the list.
- `ItemInfoWindow.Show` called repeatedly → latest item replaces current
  contents; a new async load supersedes any in-flight one (guard with the current
  item id so a stale response doesn't overwrite a newer one).

## Files

- **New:** `Windows/ItemInfoWindow.cs`, `Services/ContextMenuService.cs`
- **Modified:** `Windows/SearchWindow.cs`, `Plugin.cs`, `QiqirnCompanion.json`

## Testing

Manual, in-game (no unit-test harness for this plugin):

- `/qiqirn iron ingot` → opens the item info window directly (exact match).
- `/qiqirn iron` → opens the search window with the results list.
- `/qiqirn` → toggles the main window (unchanged).
- Right-click an item in bags → "Qiqirn Search" → info window.
- Right-click a chat item link → "Qiqirn Search" → info window.
- Right-click an item on the market board → "Qiqirn Search" → info window.
- Confirm a clean Release/Debug build via the existing dev-deploy target.

## Out of scope

- The plugin's own in-table ImGui right-click menu (`ItemInteractions`) is
  unchanged.
- No new API endpoints; reuses `SearchItemsAsync` and `GetItemSourcesAsync`.
