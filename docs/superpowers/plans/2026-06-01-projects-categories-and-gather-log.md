# Projects categories/filters + Gather Log — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reverse/bare list-paste parsing, Category + Job columns with a filter bar (incl. a hide-crystals toggle) to the Projects tab, and a plugin-wide "Gather Item" right-click that opens the game's native Gathering Log.

**Architecture:** Plugin-side only. The tasks table already receives `source` and `meta.job` per task from the backend, so Category/Job are pure functions of in-memory data — no new network calls. Gatherability is a local Lumina lookup; opening the log is a native `AgentGatheringNote` call mirroring the existing `OpenRecipeLog`.

**Tech Stack:** C# / .NET 10, Dalamud plugin API 15, Dalamud.Bindings.ImGui, FFXIVClientStructs, Lumina.Excel.

> **Testing note:** This repo has **no automated test project** (game-DLL references make one impractical). Verification per task is `dotnet build` succeeding, plus the manual in-game checks in Task 8. Each task ends with a commit.

> **Build command (used in every task):**
> `dotnet build QiqirnCompanion.csproj -c Debug`
> Expected: `Build succeeded.` with `0 Error(s)`. (Requires the local XIVLauncher Dalamud DLLs, which are present on the dev machine.)

---

### Task 1: List parser — both orderings + bare numbers

**Files:**
- Modify: `Windows/MainWindow.cs:651-668` (the `ListLineRegex` field + `ParseList`)
- Modify: `Windows/MainWindow.cs:580` (paste hint text)
- Modify: `Windows/MainWindow.cs:675` (empty-result error text)

- [ ] **Step 1: Replace the single regex + `ParseList` with the ordered-pattern version**

Replace the block currently at `Windows/MainWindow.cs:651-668`:

```csharp
    // Parse list lines in either order, with or without an "x" marker. Patterns
    // are tried in order and the FIRST match wins, so "x"-marked forms beat
    // bare-number forms — that keeps names with embedded digits intact
    // (e.g. "Grade 4 Tincture x3" → qty 3, not 4). Skips blank lines and lines
    // with qty < 1. Returns the parsed (name, qty) pairs and the count of
    // unparseable lines.
    private static readonly Regex[] ListLineRegexes =
    [
        new(@"^\s*(?<name>.+?)\s*[xX×]\s*(?<qty>\d+)\s*$", RegexOptions.Compiled), // Iron Ore x12
        new(@"^\s*(?<qty>\d+)\s*[xX×]\s*(?<name>.+?)\s*$", RegexOptions.Compiled), // 12x Iron Ore
        new(@"^\s*(?<name>.+?)\s+(?<qty>\d+)\s*$",         RegexOptions.Compiled), // Iron Ore 12
        new(@"^\s*(?<qty>\d+)\s+(?<name>.+?)\s*$",         RegexOptions.Compiled), // 12 Iron Ore
    ];

    private static (List<(string name, int qty)> items, int skipped) ParseList(string text)
    {
        var items = new List<(string name, int qty)>();
        var skipped = 0;
        foreach (var raw in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var matched = false;
            foreach (var rx in ListLineRegexes)
            {
                var m = rx.Match(raw);
                if (!m.Success) continue;
                // A matched pattern with an unusable qty is intentionally dropped
                // (not retried against looser patterns).
                if (int.TryParse(m.Groups["qty"].Value, out var qty) && qty >= 1)
                {
                    items.Add((m.Groups["name"].Value.Trim(), qty));
                    matched = true;
                }
                break;
            }
            if (!matched) skipped++;
        }
        return (items, skipped);
    }
```

- [ ] **Step 2: Update the paste hint text**

At `Windows/MainWindow.cs:580`, replace:

```csharp
        ImGui.TextDisabled("Or paste a list (e.g. \"12x Iron Ore\"):");
```

with:

```csharp
        ImGui.TextDisabled("Or paste a list (e.g. \"12x Iron Ore\" or \"Iron Ore x12\"):");
```

- [ ] **Step 3: Update the empty-result error text**

At `Windows/MainWindow.cs:675`, replace:

```csharp
            _newProjectError = "No valid lines (expected e.g. \"12x Iron Ore\").";
```

with:

```csharp
            _newProjectError = "No valid lines (expected e.g. \"12x Iron Ore\" or \"Iron Ore x12\").";
```

- [ ] **Step 4: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add Windows/MainWindow.cs
git commit -m "feat: parse pasted lists in both orderings + bare quantities"
```

---

### Task 2: Category + Job mapping helpers

**Files:**
- Modify: `Windows/MainWindow.cs` (add two static helpers near `ResolveAssignee`, around `MainWindow.cs:337`)

- [ ] **Step 1: Add the two pure helpers**

Insert immediately after the `ResolveAssignee` method (after `Windows/MainWindow.cs:347`):

```csharp
    // Maps a backend task source ("craft"/"gather"/…) to the Category column /
    // filter label. Mirrors ffxiv-helper's craftRender.ts source set.
    private static string CategoryLabel(string? source) => source switch
    {
        "craft"    => "Craft",
        "workshop" => "Workshop",
        "gather"   => "Gather",
        "market"   => "Market",
        "vendor"   => "Vendor",
        "currency" => "Currency",
        _          => "—",
    };

    // Maps a task's crafter job code (meta.job) to a full job name. Only craft/
    // workshop tasks carry one; gather/market/vendor/currency show an em dash.
    private static string JobLabel(TaskMeta? meta) => meta?.Job switch
    {
        "CRP" => "Carpenter",
        "BSM" => "Blacksmith",
        "ARM" => "Armorer",
        "GSM" => "Goldsmith",
        "LTW" => "Leatherworker",
        "WVR" => "Weaver",
        "ALC" => "Alchemist",
        "CUL" => "Culinarian",
        "ANY" => "Any",
        _     => "—",
    };
```

- [ ] **Step 2: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` (helpers are unused for now — a warning is acceptable; they're consumed in Task 3.)

- [ ] **Step 3: Commit**

```bash
git add Windows/MainWindow.cs
git commit -m "feat: add Category/Job label helpers for project tasks"
```

---

### Task 3: Tasks table — two new sortable columns

**Files:**
- Modify: `Windows/MainWindow.cs:244-333` (`DrawTasksTable`)
- Modify: `Windows/MainWindow.cs:349-366` (`SortTasksIfNeeded`)

- [ ] **Step 1: Bump the column count and add the two column headers**

At `Windows/MainWindow.cs:255`, change the column count from `5` to `7`:

```csharp
        if (!ImGui.BeginTable("##tasks", 7, flags, new Vector2(0, tableHeight))) return;
```

Then replace the column setup block at `Windows/MainWindow.cs:257-261`:

```csharp
        ImGui.TableSetupColumn("Item",     ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Job",      ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Qty",      ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status",   ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Assignee", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("",         ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 200);
```

- [ ] **Step 2: Render the two new cells and shift the existing column indices**

Replace the per-row rendering block at `Windows/MainWindow.cs:272-289` (from `ImGui.TableSetColumnIndex(0);` down to and including the assignee cell) with:

```csharp
            ImGui.TableSetColumnIndex(0);
            ImGui.Selectable(task.ItemName);
            ItemInteractions.HandleRow((uint)task.ItemId, task.ItemName);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(CategoryLabel(task.Source));

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(JobLabel(task.Meta));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted($"{task.QtyDone}/{task.QtyNeeded}");

            ImGui.TableSetColumnIndex(4);
            var statusColor = task.Status switch
            {
                "done"    => new Vector4(0.4f, 0.9f, 0.4f, 1),
                "claimed" => new Vector4(0.9f, 0.9f, 0.4f, 1),
                _         => new Vector4(1,    1,    1,    1),
            };
            ImGui.TextColored(statusColor, task.Status);

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(ResolveAssignee(detail, task));
```

Then update the actions-cell index at `Windows/MainWindow.cs:291` from `4` to `6`:

```csharp
            ImGui.TableSetColumnIndex(6);
```

- [ ] **Step 3: Re-map the sort comparators**

Replace the `switch` in `SortTasksIfNeeded` at `Windows/MainWindow.cs:356-363`:

```csharp
        Comparison<ApiTask> cmp = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => string.Compare(CategoryLabel(a.Source), CategoryLabel(b.Source), StringComparison.OrdinalIgnoreCase),
            2 => (a, b) => string.Compare(JobLabel(a.Meta), JobLabel(b.Meta), StringComparison.OrdinalIgnoreCase),
            3 => (a, b) => a.QtyNeeded.CompareTo(b.QtyNeeded),
            4 => (a, b) => string.Compare(a.Status, b.Status, StringComparison.OrdinalIgnoreCase),
            5 => (a, b) => string.Compare(a.AssigneeId ?? "", b.AssigneeId ?? "", StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => 0,
        };
```

- [ ] **Step 4: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add Windows/MainWindow.cs
git commit -m "feat: show sortable Category/Job columns in project tasks"
```

---

### Task 4: Filter bar (Category + Job dropdowns, hide-crystals, clear)

**Files:**
- Modify: `Windows/MainWindow.cs:32-33` (add filter state fields)
- Modify: `Windows/MainWindow.cs:188-192` (call `DrawProjectFilters`)
- Modify: `Windows/MainWindow.cs` (add `DrawProjectFilters` method)
- Modify: `Windows/MainWindow.cs:266-268` (row-skip predicates in `DrawTasksTable`)

- [ ] **Step 1: Add filter state fields and crystal-id constants**

Insert after `Windows/MainWindow.cs:33` (right after the `_progressAmounts` field):

```csharp

    // Projects filter bar state.
    private string _filterCategory = "All";
    private string _filterJob      = "All";
    private bool   _hideCrystals   = false;

    // Elemental shards (2-7), crystals (8-13) and clusters (14-19) occupy this
    // contiguous item-id block.
    private const int CrystalIdMin = 2;
    private const int CrystalIdMax = 19;
```

- [ ] **Step 2: Add the `DrawProjectFilters` method**

Insert immediately before `DrawTasksTable` (before `Windows/MainWindow.cs:244`):

```csharp
    // Filter row above the tasks table. Dropdown options are built from the
    // tasks actually present so no empty option is ever offered; if a previously
    // selected value is gone after loading another project, it resets to "All".
    private void DrawProjectFilters(ApiProjectDetail detail)
    {
        var categories = new List<string> { "All" };
        foreach (var t in detail.Tasks)
        {
            var c = CategoryLabel(t.Source);
            if (!categories.Contains(c)) categories.Add(c);
        }
        if (!categories.Contains(_filterCategory)) _filterCategory = "All";

        var jobs = new List<string> { "All" };
        foreach (var t in detail.Tasks)
        {
            var j = JobLabel(t.Meta);
            if (j != "—" && !jobs.Contains(j)) jobs.Add(j);
        }
        if (!jobs.Contains(_filterJob)) _filterJob = "All";

        var catIdx = Math.Max(0, categories.IndexOf(_filterCategory));
        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("Category##filter", ref catIdx, categories.ToArray(), categories.Count))
            _filterCategory = categories[catIdx];

        ImGui.SameLine();
        var jobIdx = Math.Max(0, jobs.IndexOf(_filterJob));
        ImGui.SetNextItemWidth(140);
        if (ImGui.Combo("Job##filter", ref jobIdx, jobs.ToArray(), jobs.Count))
            _filterJob = jobs[jobIdx];

        ImGui.SameLine();
        ImGui.Checkbox("Hide crystals", ref _hideCrystals);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hide shards, crystals and clusters");

        ImGui.SameLine();
        if (ImGui.SmallButton("× Clear"))
        {
            _filterCategory = "All";
            _filterJob      = "All";
            _hideCrystals   = false;
        }
    }
```

- [ ] **Step 3: Call the filter bar between the phase bar and the table**

Replace `Windows/MainWindow.cs:188-192`:

```csharp
        if (_projectDetail is not null)
        {
            DrawPhaseBar(_projectDetail);
            DrawProjectFilters(_projectDetail);
            DrawTasksTable(_projectDetail);
        }
```

- [ ] **Step 4: Apply the filters in the row loop**

In `DrawTasksTable`, the loop currently starts at `Windows/MainWindow.cs:266-268`:

```csharp
        foreach (var task in detail.Tasks)
        {
            if (_selectedPhaseKey != null && PhaseKeyOf(task) != _selectedPhaseKey) continue;
```

Replace those three lines with:

```csharp
        foreach (var task in detail.Tasks)
        {
            if (_selectedPhaseKey != null && PhaseKeyOf(task) != _selectedPhaseKey) continue;
            if (_filterCategory != "All" && CategoryLabel(task.Source) != _filterCategory) continue;
            if (_filterJob != "All" && JobLabel(task.Meta) != _filterJob) continue;
            if (_hideCrystals && task.ItemId is >= CrystalIdMin and <= CrystalIdMax) continue;
```

- [ ] **Step 5: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add Windows/MainWindow.cs
git commit -m "feat: add category/job/crystal filters to projects view"
```

---

### Task 5: `GatheringData` local lookup + wiring

**Files:**
- Create: `Services/GatheringData.cs`
- Modify: `Plugin.cs:52` (initialize alongside `ItemInteractions`)

- [ ] **Step 1: Create the service**

Create `Services/GatheringData.cs`:

```csharp
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
```

> If `dotnet build` reports that `GatheringItem.Item` has no `RowId` in this Lumina version, it is a `RowRef` whose id is reached the same way — try `row.Item.Row` (a `uint`) instead. Pick whichever the compiler accepts; both are the referenced Item row id.

- [ ] **Step 2: Initialize it in `Plugin.cs`**

At `Plugin.cs:52`, immediately after `ItemInteractions.Initialize(chatGui);`, add:

```csharp
        GatheringData.Initialize(dataManager);
```

- [ ] **Step 3: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add Services/GatheringData.cs Plugin.cs
git commit -m "feat: build local gatherable-item lookup from Lumina"
```

---

### Task 6: `GameActions.OpenGatheringLog`

**Files:**
- Modify: `Services/GameActions.cs` (add method after `OpenRecipeLog`, around `GameActions.cs:34`)

- [ ] **Step 1: Add the native call**

Insert after the `OpenRecipeLog` method (after `Services/GameActions.cs:34`):

```csharp
    /// <summary>
    /// Open the in-game Gathering Log to this item (native "Gathering Log"),
    /// which shows the zones and nodes where it can be collected.
    /// </summary>
    public static unsafe void OpenGatheringLog(uint itemId)
    {
        AgentGatheringNote.Instance()->OpenGatherableByItemId((ushort)itemId);
    }
```

> `AgentGatheringNote` lives in `FFXIVClientStructs.FFXIV.Client.UI.Agent`, already imported at `GameActions.cs:3`. If `dotnet build` reports an argument-type mismatch on `OpenGatherableByItemId`, adjust the cast to the type the signature expects (it is the item row id).

- [ ] **Step 2: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Services/GameActions.cs
git commit -m "feat: add OpenGatheringLog native game action"
```

---

### Task 7: "Gather Item" on both right-click surfaces

**Files:**
- Modify: `Services/ItemInteractions.cs:67-68` (in-plugin table menu)
- Modify: `Services/ContextMenuService.cs:39-46` (native game menu)

- [ ] **Step 1: Add the entry to the in-plugin table context menu**

In `ItemInteractions.DrawContextMenu`, after the recipe-log item at `Services/ItemInteractions.cs:67-68`:

```csharp
        if (ImGui.MenuItem("Search Recipes / Open Recipe Log"))
            GameActions.OpenRecipeLog(itemId);
```

add:

```csharp
        if (GatheringData.IsGatherable(itemId) && ImGui.MenuItem("Open Gathering Log"))
            GameActions.OpenGatheringLog(itemId);
```

(`GatheringData` and `GameActions` are in the same `QiqirnCompanion.Services` namespace — no new using needed.)

- [ ] **Step 2: Add the entry to the native game context menu**

In `ContextMenuService.OnMenuOpened`, after the existing `args.AddMenuItem(...)` block that ends at `Services/ContextMenuService.cs:46`, add:

```csharp
        if (GatheringData.IsGatherable(itemId))
        {
            args.AddMenuItem(new MenuItem
            {
                Name        = "Gather Item",
                PrefixChar  = 'Q',
                PrefixColor = 706,
                OnClicked   = _ => GameActions.OpenGatheringLog(itemId),
            });
        }
```

> `itemId` is already in scope from `TryResolveItemId` at the top of `OnMenuOpened` (`ContextMenuService.cs:34`). `GameActions`/`GatheringData` are in the same namespace.

- [ ] **Step 3: Build**

Run: `dotnet build QiqirnCompanion.csproj -c Debug`
Expected: `Build succeeded.` `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add Services/ItemInteractions.cs Services/ContextMenuService.cs
git commit -m "feat: add Gather Item right-click that opens the Gathering Log"
```

---

### Task 8: Manual in-game verification

**Files:** none (verification only)

- [ ] **Step 1: Reload the plugin**

In game: `/xldev` → Dev Plugins → reload **QiqirnCompanion** (the Debug build is auto-copied to `devPlugins` by the `DeployToDevPlugins` MSBuild target).

- [ ] **Step 2: Verify list parsing**

Open Projects → ＋ New Project → paste into the list box:

```
12x Iron Ore
Iron Ore x12
Iron Ore 12
12 Iron Ore
Grade 4 Tincture x3
Copper Ore
0x Bronze Ingot
```

Click "Create from list". Expected: 5 valid items created (the four Iron Ore lines at qty 12, and Grade 4 Tincture at qty **3**); `Copper Ore` (no qty) and `0x Bronze Ingot` (qty < 1) are skipped.

- [ ] **Step 3: Verify columns + sorting**

Open a project with crafted **and** gathered tasks. Expected: Category shows Craft/Gather/etc.; Job shows the crafter (e.g. Blacksmith) for craft tasks and `—` for gather/market/vendor/currency. Click the **Category** and **Job** headers — rows re-sort by the displayed label.

- [ ] **Step 4: Verify filters**

Pick a Category in the dropdown → only that category's rows show. Pick a Job → narrows further. Tick **Hide crystals** → shard/crystal/cluster rows disappear. Confirm filters compose with the phase selector on a multi-phase (company-craft) project. Click **× Clear** → all rows return.

- [ ] **Step 5: Verify "Gather Item"**

Right-click a known gatherable (e.g. **Copper Ore**) both in a plugin table row and in your inventory. Expected: "Open Gathering Log" (plugin menu) / "Gather Item" (native menu) appears and opens the in-game Gathering Log at that item, showing its zones. Right-click a non-gatherable (e.g. a finished gear piece) → the entry is **absent**.

- [ ] **Step 6: Final regression build**

Run: `dotnet build QiqirnCompanion.csproj -c Release`
Expected: `Build succeeded.` `0 Error(s)` (also exercises the `PackagePlugin` target).
