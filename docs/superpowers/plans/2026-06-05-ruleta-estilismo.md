# Ruleta del Estilismo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a hidden `/qiqirn ruleta` window that rolls a random hairstyle/color/highlights assignment and copies a Spanish summary line to the clipboard.

**Architecture:** One pure helper (`RuletaRoll`) holds the randomization and message formatting; one `Window` subclass (`RuletaWindow`) draws the ImGui form and writes the clipboard; `Plugin.cs` constructs the window, registers it with the existing `WindowSystem`, and special-cases the `ruleta` subcommand in `OnCommand`. Nothing is auto-sent to chat — the MC pastes the clipboard text manually.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), Dalamud plugin framework, `Dalamud.Bindings.ImGui`.

---

## Testing note

This repo has **no** automated test harness — Dalamud plugins run inside FFXIV, and a unit-test project would have to drag in the Dalamud runtime. Per the approved spec, verification is `dotnet build` (compile-checks every task) plus a final scripted manual in-game pass. The "verify" step in each task is therefore a clean build, not a test runner.

Build command (from repo root):

```
dotnet build
```

Expected on success: `Build succeeded.` with `0 Error(s)`. A local build also auto-copies the DLL into `%AppData%\XIVLauncher\devPlugins\QiqirnCompanion` (see the `DeployToDevPlugins` target in `QiqirnCompanion.csproj`), so `/xldev` → Reload picks it up in-game.

## File Structure

- **Create** `Windows/RuletaRoll.cs` — pure `readonly record struct` + static `Roll` / `Format`. Grid constants live here. No ImGui.
- **Create** `Windows/RuletaWindow.cs` — `Window` subclass; the ImGui form, roll trigger, clipboard copy.
- **Modify** `Plugin.cs` — add a `RuletaWindow` field, construct it, add it to `_windowSystem`, and route `/qiqirn ruleta` to it in `OnCommand`.

---

### Task 1: RuletaRoll pure helper

**Files:**
- Create: `Windows/RuletaRoll.cs`

- [ ] **Step 1: Create the helper file**

Create `Windows/RuletaRoll.cs` with this exact content:

```csharp
using System;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Pure roll + formatting logic for the Ruleta del Estilismo feature. No ImGui
/// here — kept isolated so the randomization and message format stay trivially
/// correct and independent of the window's draw loop.
/// </summary>
public readonly record struct RuletaRoll(
    int Cut, int Count, int BaseRow, int BaseCol, int HiRow, int HiCol)
{
    /// <summary>Hair-color grid dimensions from the in-game appearance menu.</summary>
    public const int Columns = 8;
    public const int Rows    = 24;

    /// <summary>
    /// Roll a full styling assignment. <paramref name="count"/> is the person's
    /// total available haircuts; the caller guarantees it is &gt;= 1.
    /// </summary>
    public static RuletaRoll Roll(int count, Random rng) => new(
        Cut:     rng.Next(1, count + 1),
        Count:   count,
        BaseRow: rng.Next(1, Rows + 1),
        BaseCol: rng.Next(1, Columns + 1),
        HiRow:   rng.Next(1, Rows + 1),
        HiCol:   rng.Next(1, Columns + 1));

    /// <summary>
    /// Build the paste-ready Spanish message. ASCII only — FFXIV chat filters
    /// some Unicode, and none of these words need accents.
    /// </summary>
    public static string Format(string name, RuletaRoll r) =>
        $"Ruleta del Estilismo - {name} | Corte: {r.Cut}/{r.Count} | " +
        $"Color base: F{r.BaseRow} C{r.BaseCol} | Mechas: F{r.HiRow} C{r.HiCol}";
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Windows/RuletaRoll.cs
git commit -m "feat: add RuletaRoll pure roll + message helper"
```

---

### Task 2: RuletaWindow

**Files:**
- Create: `Windows/RuletaWindow.cs`

Pattern references: `Windows/SearchWindow.cs` (Window subclass, `InputTextWithHint`, `IDisposable` with empty body), `Windows/MainWindow.cs:378` (conditional `BeginDisabled`/`EndDisabled` around a button).

- [ ] **Step 1: Create the window file**

Create `Windows/RuletaWindow.cs` with this exact content:

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Hidden "easter egg" window for the guild's weekly Ruleta del Estilismo.
/// Reachable only via <c>/qiqirn ruleta</c>. The MC enters a name and the
/// person's haircut count, rolls a styling assignment, and copies a Spanish
/// summary to the clipboard to paste into chat. Nothing is auto-sent.
/// </summary>
public class RuletaWindow : Window, IDisposable
{
    private readonly Random _rng = new();

    private string      _name    = "";
    private int         _count   = 0;
    private RuletaRoll? _result  = null;
    private string      _message = "";

    public RuletaWindow() : base("Ruleta del Estilismo")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 200),
            MaximumSize = new Vector2(900, 400),
        };
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Introduce el nombre y los cortes disponibles, y dale a Daleee.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        // Editing the name after a roll keeps the preview line in sync.
        if (ImGui.InputTextWithHint("##nombre", "Nombre", ref _name, 64) && _result != null)
            _message = RuletaRoll.Format(_name, _result.Value);

        ImGui.SetNextItemWidth(160);
        ImGui.InputInt("Cortes disponibles", ref _count);
        if (_count < 0) _count = 0;

        ImGui.Spacing();

        var canRoll = _count >= 1;
        if (!canRoll) ImGui.BeginDisabled();
        if (ImGui.Button("Daleee", new Vector2(120, 0)))
        {
            _result  = RuletaRoll.Roll(_count, _rng);
            _message = RuletaRoll.Format(_name, _result.Value);
        }
        if (!canRoll) ImGui.EndDisabled();

        if (_result == null) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped(_message);
        ImGui.Spacing();
        if (ImGui.Button("Copiar", new Vector2(120, 0)))
            ImGui.SetClipboardText(_message);
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Windows/RuletaWindow.cs
git commit -m "feat: add hidden Ruleta del Estilismo window"
```

---

### Task 3: Wire into Plugin.cs

**Files:**
- Modify: `Plugin.cs` (field declaration block ~line 26, constructor window block ~lines 62-75, `OnCommand` ~lines 92-105)

- [ ] **Step 1: Add the field**

In `Plugin.cs`, in the `private readonly ... Window` field group, add a `RuletaWindow` field after the existing window fields. Change:

```csharp
    private readonly CleanupWindow _cleanupWindow;
    private readonly SalesTracker  _salesTracker;
```

to:

```csharp
    private readonly CleanupWindow _cleanupWindow;
    private readonly RuletaWindow  _ruletaWindow;
    private readonly SalesTracker  _salesTracker;
```

- [ ] **Step 2: Construct and register the window**

In the constructor's `// Windows` block, after `_cleanupWindow = new CleanupWindow(_api);`, add construction. Change:

```csharp
        _cleanupWindow = new CleanupWindow(_api);
        _mainWindow    = new MainWindow(Config, _api, playerState, _searchWindow, _tradingWindow, _plannerWindow, _cleanupWindow, _settingsPanel);
```

to:

```csharp
        _cleanupWindow = new CleanupWindow(_api);
        _ruletaWindow  = new RuletaWindow();
        _mainWindow    = new MainWindow(Config, _api, playerState, _searchWindow, _tradingWindow, _plannerWindow, _cleanupWindow, _settingsPanel);
```

Then, after `_windowSystem.AddWindow(_cleanupWindow);`, register it. Change:

```csharp
        _windowSystem.AddWindow(_cleanupWindow);
```

to:

```csharp
        _windowSystem.AddWindow(_cleanupWindow);
        _windowSystem.AddWindow(_ruletaWindow);
```

(Note: it is deliberately NOT added to any `MainWindow` button — it stays hidden.)

- [ ] **Step 3: Route the hidden subcommand**

In `OnCommand`, add the `ruleta` case after the empty-query check and before the search path. Change:

```csharp
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
```

to:

```csharp
        var query = args.Trim();
        if (query.Length == 0)
        {
            ToggleMain();
            return;
        }

        // Hidden easter egg: /qiqirn ruleta opens the styling-roulette window.
        // Must be checked before the search path, or it would search for an
        // item literally named "ruleta".
        if (query.Equals("ruleta", StringComparison.OrdinalIgnoreCase))
        {
            _ruletaWindow.Toggle();
            return;
        }

        // Open the standalone search window and run the query. On an exact name
        // match, RunQuery's completion path jumps straight to the info window.
        _searchWindow.IsOpen = true;
        _searchWindow.RunQuery(query);
```

- [ ] **Step 4: Confirm the `System` namespace is available for `StringComparison`**

`StringComparison` lives in `System`. Check the top of `Plugin.cs` for a `using System;`. If it is absent, add `using System;` to the using block. (As of this writing `Plugin.cs` does not import `System`, so this step will normally add it.)

- [ ] **Step 5: Verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add Plugin.cs
git commit -m "feat: route /qiqirn ruleta to the hidden styling window"
```

---

### Task 4: Manual in-game verification

**Files:** none (verification only)

No automated tests exist for this layer; this task is the acceptance pass. Build is already deployed to `devPlugins` by Task 3's build.

- [ ] **Step 1: Reload the plugin**

In-game, run `/xldev`, find Qiqirn Companion, and Reload (or toggle off/on). If using a fresh devPlugins install, enable it once via `/xldev` → Installed Dev Plugins.

- [ ] **Step 2: Open the hidden window**

Run `/qiqirn ruleta`. Expected: the "Ruleta del Estilismo" window opens. Run `/qiqirn ruleta` again. Expected: it closes (Toggle).

- [ ] **Step 3: Regression — search still works**

Run `/qiqirn ash`. Expected: the Item Search window opens and searches normally (the `ruleta` case did not break the search path). Run `/qiqirn` with no args. Expected: the main window toggles.

- [ ] **Step 4: Daleee disabled at zero**

With `Cortes disponibles` at `0`, confirm the **Daleee** button is greyed out / not clickable.

- [ ] **Step 5: Roll and check ranges**

Enter a name (e.g. `Lulu`) and a count (e.g. `73`). Click **Daleee** several times. Confirm each result line shows: `Corte: N/73` with `1 <= N <= 73`; `Color base: F<1-24> C<1-8>`; `Mechas: F<1-24> C<1-8>`.

- [ ] **Step 6: Copy and paste**

Click **Copiar**, then paste into a chat box (e.g. `/say` — do NOT send, just paste). Confirm the pasted text matches the preview exactly, with no boxed/dropped characters. Example shape:
`Ruleta del Estilismo - Lulu | Corte: 27/73 | Color base: F12 C1 | Mechas: F1 C5`

- [ ] **Step 7: Name-after-roll sync**

After a roll, edit the `Nombre` field. Confirm the preview line updates to the new name without needing another roll.

---

## Self-Review

- **Spec coverage:** Hidden command routing (Task 3) ✓; plugin rolls cut/base/highlights with correct ranges (Task 1) ✓; haircut count MC-entered, count<1 disables roll (Tasks 2, 4) ✓; clipboard-only output, no auto-send (Task 2) ✓; Spanish ASCII message with name + picks, `Daleee`/`Copiar` labels (Tasks 1, 2) ✓; not added to any menu (Task 3 note) ✓; minimal — no history/re-roll UI ✓; manual verification incl. search regression (Task 4) ✓.
- **Placeholder scan:** none — every code step shows complete content.
- **Type consistency:** `RuletaRoll` record + `Roll(int, Random)` + `Format(string, RuletaRoll)` and field names (`_name`, `_count`, `_result`, `_message`, `_rng`) are used identically across Tasks 1–2; `_ruletaWindow` consistent across Task 3.

## Out of scope / follow-up

Cutting a release (version bump + git tag) is the existing maintainer flow in `README.md` and is intentionally not part of this plan.
