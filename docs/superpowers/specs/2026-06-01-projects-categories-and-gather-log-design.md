# Projects: reverse list-paste, Category/Job columns + filters, and "Gather Item" context menu

**Date:** 2026-06-01
**Repo:** qiqirn-companion (Dalamud plugin)
**Status:** Approved design — ready for implementation plan

## Goal

Four related improvements, three in the **Projects** tab and one plugin-wide:

1. **List importer** accepts quantities in both orderings (`12x Iron Ore` *and*
   `Iron Ore x12`), plus bare-number variants.
2. **Category** and **Job** columns on the project tasks table, derived from data
   the backend already sends — no new API calls.
3. A **filter bar** at the top of the Projects view: Category dropdown, Job
   dropdown, and a "Hide crystals/shards/clusters" checkbox. Sorting by clicking
   column headers already works and now covers the new columns.
4. A plugin-wide **"Gather Item"** right-click option that opens the game's native
   Gathering Log for gatherable items (shows where the item can be collected).

This is **plugin-side only** — no backend (`ffxiv-helper`) changes.

## Current state (relevant facts)

- The list parser is `ListLineRegex` + `ParseList` at `Windows/MainWindow.cs:653`.
  Current regex `^\s*(\d+)\s*[xX×]\s*(.+?)\s*$` matches only the **prefix** form
  (`12x Iron Ore`). The hint/error strings say `"12x Iron Ore"`
  (`MainWindow.cs:580`, `:675`). Parsing is fully client-side; the backend
  `CreateProjectFromListAsync` receives `(name, qty)` pairs.
- `DrawTasksTable` (`MainWindow.cs:244`) renders a 5-column sortable table (Item,
  Qty, Status, Assignee, actions). `SortTasksIfNeeded` (`MainWindow.cs:349`) maps
  column index → comparator. The per-row loop already skips by
  `_selectedPhaseKey`.
- `ApiTask` (`Services/ApiClient.cs:29`) already deserializes `Source` (string?)
  and `Meta.Job` (string?) — but nothing in the plugin displays them yet.
- Backend source of truth (`ffxiv-helper`): `StoredTask` carries
  `source: 'craft' | 'workshop' | 'market' | 'vendor' | 'currency' | 'gather'`
  and `meta.job: CrafterCode` (`'CRP' | 'BSM' | 'ARM' | 'GSM' | 'LTW' | 'WVR' |
  'ALC' | 'CUL' | 'ANY'`). `getProjectDetail` returns the full `StoredTask[]`, so
  both fields reach the plugin. The web's `craftRender.ts` is the canonical
  label/emoji mapping.
- `MainWindow` has no `IDataManager` (`MainWindow.cs:55`). The elemental
  shards/crystals/clusters are exactly item IDs **2–19** (6 shards, 6 crystals,
  6 clusters), so the crystal filter needs no Lumina dependency.
- `GameActions` (`Services/GameActions.cs`) already wraps native calls, e.g.
  `OpenRecipeLog` → `AgentRecipeNote.Instance()->SearchRecipeByItemId(itemId)`.
- `FFXIVClientStructs` exposes `AgentGatheringNote` with `OpenGatherableByItemId`.
- Lumina has the `GatheringItem` sheet (maps gatherable items). Mining/botany
  gatherables live here; fish use a separate Fishing Log (out of scope).
- Two right-click surfaces exist: the **native** game menu
  (`Services/ContextMenuService.cs`, has `IDataManager`) and the **in-plugin**
  ImGui table menu (`Services/ItemInteractions.cs`, static, no `IDataManager`).
  Both are wired in `Plugin.cs`; `ItemInteractions.Initialize(chatGui)` is the
  established static-init pattern, and `IDataManager` is available there
  (`Plugin.cs:38`).

## Components

### 1. List parser — both orderings + bare numbers (`MainWindow.cs`)

Replace the single `ListLineRegex` with an ordered list of compiled patterns
tried per line; **first match wins**. `x`-marked forms are tried before bare
forms so embedded digits in names resolve correctly (e.g. `Grade 4 Tincture x3`
→ qty 3, not 4):

1. Suffix + x: `^\s*(?<name>.+?)\s*[xX×]\s*(?<qty>\d+)\s*$`
2. Prefix + x: `^\s*(?<qty>\d+)\s*[xX×]\s*(?<name>.+?)\s*$`
3. Suffix bare: `^\s*(?<name>.+?)\s+(?<qty>\d+)\s*$`
4. Prefix bare: `^\s*(?<qty>\d+)\s+(?<name>.+?)\s*$`

`ParseList` iterates the patterns, parses `qty` (skip if `< 1`), trims `name`,
skips blank/unparseable lines and counts them in `skipped` (unchanged contract:
returns `(List<(string name, int qty)> items, int skipped)`). Update the hint
(`MainWindow.cs:580`) and the empty-result error (`MainWindow.cs:675`) to show
both forms, e.g. `"12x Iron Ore" or "Iron Ore x12"`.

### 2. Category + Job mapping helpers (`MainWindow.cs`)

Two pure static helpers (no I/O):

- `CategoryLabel(string? source)` →
  `craft`→`"Craft"`, `workshop`→`"Workshop"`, `gather`→`"Gather"`,
  `market`→`"Market"`, `vendor`→`"Vendor"`, `currency`→`"Currency"`,
  null/unknown→`"—"`.
- `JobLabel(TaskMeta? meta)` → maps `meta.Job` (CrafterCode) to a full name
  (`CRP`→`"Carpenter"`, `BSM`→`"Blacksmith"`, `ARM`→`"Armorer"`,
  `GSM`→`"Goldsmith"`, `LTW`→`"Leatherworker"`, `WVR`→`"Weaver"`,
  `ALC`→`"Alchemist"`, `CUL`→`"Culinarian"`, `ANY`→`"Any"`). Missing/empty
  (gather, market, vendor, currency) → `"—"`.

### 3. Tasks table: two new sortable columns (`DrawTasksTable`)

Column count 5 → 7, new order and widths:

| # | Column   | Flags                                   |
|---|----------|-----------------------------------------|
| 0 | Item     | WidthStretch                            |
| 1 | Category | WidthFixed 90                           |
| 2 | Job      | WidthFixed 110                          |
| 3 | Qty      | WidthFixed 60                           |
| 4 | Status   | WidthFixed 70                           |
| 5 | Assignee | WidthFixed 140                          |
| 6 | (actions)| WidthFixed 200, NoSort                  |

Render `CategoryLabel`/`JobLabel` in the new cells. `SortTasksIfNeeded` switch
re-mapped to the new indices; add comparators for Category (1) and Job (2) that
`string.Compare` the display labels (OrdinalIgnoreCase). Existing comparators
shift to their new indices (Qty 3, Status 4, Assignee 5).

### 4. Filter bar (`DrawProjectsTab` / new `DrawProjectFilters`)

New state fields on `MainWindow`:
`private string _filterCategory = "All";`,
`private string _filterJob = "All";`,
`private bool _hideCrystals = false;`.

A `DrawProjectFilters(ApiProjectDetail detail)` drawn above the table (after the
phase bar, inside the `_projectDetail is not null` block):

- **Category** combo: `"All"` + the distinct `CategoryLabel` values present in
  `detail.Tasks` (built each frame from the loaded tasks so no empty option is
  offered).
- **Job** combo: `"All"` + distinct `JobLabel` values present (excluding `"—"`).
- **"Hide crystals/shards/clusters"** checkbox bound to `_hideCrystals`.
- **"× Clear"** button resets all three to defaults.

In the `DrawTasksTable` row loop, alongside the existing phase skip, also skip a
task when:
- `_filterCategory != "All"` and `CategoryLabel(task.Source) != _filterCategory`,
- `_filterJob != "All"` and `JobLabel(task.Meta) != _filterJob`,
- `_hideCrystals` and `task.ItemId is >= 2 and <= 19` (named const
  `CrystalIdMin = 2`, `CrystalIdMax = 19`, with a comment naming them as the
  elemental shards/crystals/clusters).

Filters, phase selection, and sorting compose because each is independent
(row-skip predicates + an in-place sort over the same `detail.Tasks` list).

### 5. `Services/GatheringData.cs` (new)

Single responsibility: tell callers whether an item ID is a (mining/botany)
gatherable. Mirrors the `ItemInteractions.Initialize` static pattern.

- `static void Initialize(IDataManager data)` — builds, once, a
  `HashSet<uint>` from `data.GetExcelSheet<GatheringItem>()`, adding each row's
  referenced item ID (read the row's `Item` field's row id; guard against the
  0/placeholder rows). Wrapped in try/catch — on failure the set stays empty and
  `IsGatherable` simply returns false (feature degrades silently).
- `static bool IsGatherable(uint itemId)` — `_set.Contains(itemId)`.

Wired in `Plugin.cs` next to `ItemInteractions.Initialize(chatGui)`:
`GatheringData.Initialize(dataManager);`.

### 6. `GameActions.OpenGatheringLog` (`Services/GameActions.cs`)

```csharp
/// <summary>Open the in-game Gathering Log to this item (shows zones/nodes).</summary>
public static unsafe void OpenGatheringLog(uint itemId)
{
    AgentGatheringNote.Instance()->OpenGatherableByItemId(/* cast as required */ itemId);
}
```

Verify the exact parameter type at implementation (likely `ushort`); cast
accordingly. Add the `AgentGatheringNote` using if not already present.

### 7. "Gather Item" entries on both right-click surfaces

- **In-plugin tables** (`ItemInteractions.DrawContextMenu`,
  `Services/ItemInteractions.cs:67`): after the "Search Recipes / Open Recipe
  Log" item, add — **only when `GatheringData.IsGatherable(itemId)`** —
  `if (ImGui.MenuItem("Open Gathering Log")) GameActions.OpenGatheringLog(itemId);`.
- **Native game menu** (`ContextMenuService.OnMenuOpened`,
  `Services/ContextMenuService.cs:32`): after adding the existing "Qiqirn Search"
  item, add a second `MenuItem` named **"Gather Item"** (same `PrefixChar = 'Q'`)
  whose `OnClicked` calls `GameActions.OpenGatheringLog(itemId)` — added **only
  when `GatheringData.IsGatherable(itemId)`**.

## Data flow

Project load is unchanged: `LoadProjectDetail` → `GetProjectDetailAsync` →
`ApiProjectDetail` with `Tasks[*].Source` and `Tasks[*].Meta.Job` already
populated. Category/Job columns and filters are pure functions of that in-memory
list; the filter dropdown contents are derived from it each frame. The gather
context menu reads only the local Lumina-built set and calls a native agent.
Nothing new hits the network.

## Error handling / edge cases

- Parser: blank and unparseable lines counted in `skipped`; `qty < 1` skipped.
  `x`-before-bare ordering protects names with embedded digits.
- Unknown/missing `Source` → Category `"—"`; such tasks still appear under the
  `"All"` category filter.
- Gather tasks (and market/vendor/currency) show Job `"—"` — the data model has
  no crafter job for them; gathering does not record which gatherer (MIN vs BTN),
  only that the item is gathered.
- `GatheringData` init failure → empty set → no "Gather Item" entries (no crash).
- `OpenGatheringLog` on a non-gatherable is never reachable because the menu
  entry is gated on `IsGatherable`.

## Out of scope (possible follow-ups)

- Fishing-log items (`SpearfishingItem` / `FishParameter`).
- Collapsing the six categories into a coarse Crafting/Gathering two-bucket view.
- Showing the specific gatherer job (MIN/BTN) — not in the data.

## Testing

- `ParseList` is a pure static method. No C# test project exists in the repo, so
  verify manually against real Teamcraft output and representative cases:
  `12x Iron Ore`, `Iron Ore x12`, `Iron Ore 12`, `12 Iron Ore`,
  `Grade 4 Tincture x3` (→ qty 3), blank lines, `0x Foo` (skipped), garbage
  lines (counted in `skipped`).
- Build the plugin and confirm in-game: new columns render and sort; the three
  filters narrow the table and compose with phase selection; "Gather Item"
  appears for a known gatherable (e.g. Copper Ore) in both the inventory
  right-click and a plugin table, and opens the Gathering Log; it is absent for a
  non-gatherable (e.g. a finished crafted gear piece).
