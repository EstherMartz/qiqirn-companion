# Settings tab + configurable open-hotkey

**Date:** 2026-05-30
**Repo:** qiqirn-companion (Dalamud plugin)
**Status:** Approved design — ready for implementation plan

## Goal

1. Add a **Settings** tab to the main window that unifies all plugin settings
   (currently split into the gear-icon Config window).
2. Add a user-configurable **hotkey** that toggles the main window open/closed,
   set via **press-to-bind** capture.
3. Per the user's note, **character name** and **home world** are game-derived —
   detect them from the game client and auto-fill, keeping the manual fields only
   as optional overrides.

## Current state (relevant facts)

- `Windows/ConfigWindow.cs` is the only settings surface today (opened by the gear
  icon / `UiBuilder.OpenConfigUi`). It edits Guild ID, API URL, Character Name
  Override, Home World via edit buffers + Save/Cancel.
- `Windows/MainWindow.cs` has tabs: Trading, Search, Planner, Projects, Crafting,
  Cleanup. No Settings tab.
- `MainWindow.GetWorldName()` returns `_config.HomeWorld` only, with a comment that
  `IPlayerState` doesn't expose world directly. Trading presets read
  `_config.HomeWorld`.
- `Plugin` injects `IPlayerState`, `IChatGui`, `IDataManager`, `IContextMenu`. It
  does **not** yet inject `IFramework`, `IKeyState`, or `IClientState`.
- **Dalamud has no built-in hotkey registration.** The standard pattern is polling
  `IKeyState` each frame from an `IFramework.Update` handler, with edge detection.

## Components

### 1. `Services/HotkeyService.cs` (new)

Single owner of all game keyboard reading, driven by `IFramework.Update`.
Constructor: `HotkeyService(IFramework framework, IKeyState keyState,
Configuration config, Action toggleMain)`. Subscribes `framework.Update` in the
ctor; unsubscribes in `Dispose`.

- **Runtime detection (each Update):**
  - If no key is bound (`config.HotkeyKey == VirtualKey.NO_KEY`) → do nothing.
  - Compute `comboDown` = required modifiers held (Ctrl/Alt/Shift per config) AND
    the main key down.
  - **Edge-trigger:** keep a `_wasDown` bool; fire `toggleMain()` only on the
    transition from not-down → down. This prevents repeat toggles while held.
  - **Guard:** skip firing when `ImGui.GetIO().WantTextInput` is true (so the
    hotkey never triggers while typing in a plugin text field).
- **Press-to-bind capture:**
  - `void BeginCapture()` sets `IsCapturing = true`.
  - While capturing (inside Update): if `Esc` is down → cancel (no change). Else
    scan `keyState.GetValidVirtualKeys()` for the first key that is down and is
    **not** a modifier (Ctrl/Alt/Shift/Win); when found, record that key +
    currently-held modifiers into `config`, `config.Save()`, end capture.
  - `bool IsCapturing { get; }` for the UI to show "Press a key…".
- Modifier detection uses the combined virtual keys `VirtualKey.CONTROL`,
  `VirtualKey.MENU` (Alt), `VirtualKey.SHIFT`.

### 2. `Windows/SettingsPanel.cs` (new) — unified settings UI

A plain class with `void DrawContent()` (mirrors the existing shared
tab/standalone idiom, e.g. `SearchWindow.DrawContent`). Constructor:
`SettingsPanel(Configuration config, HotkeyService hotkey, IClientState clientState)`.

Renders, top to bottom:
- **Hotkey row:** label showing the current binding as text (e.g. `Ctrl + Q`, or
  `Unbound`). A **Set hotkey** button → `hotkey.BeginCapture()`; while
  `hotkey.IsCapturing`, the button/label shows "Press a key… (Esc to cancel)". A
  **Clear** button sets the binding to unbound and saves.
- **Guild ID** and **API URL** text fields (moved from ConfigWindow), with the
  same tooltips.
- **Character:** show the detected in-game name (`clientState.LocalPlayer?.Name`)
  as "Detected: <name>"; keep the **Character Name Override** field labelled as
  optional ("leave blank to use detected").
- **Home World:** read the detected world from the game
  (`clientState.LocalPlayer?.HomeWorld`); show "Detected: <world>". If
  `config.HomeWorld` is empty and a world is detected, auto-fill + save it so the
  user never types it. Keep the manual field as an override (for logged-out / DC
  travel).
- **Save** button: applies the buffered Guild ID / API URL / overrides to config
  and saves (same buffer+Save pattern as today). The hotkey and home-world
  auto-fill save immediately when changed, independent of this button.

Binding display helper: format `Ctrl/Alt/Shift + <KeyName>` from the config
fields; `Unbound` when `HotkeyKey == NO_KEY`.

### 3. `Windows/ConfigWindow.cs` (modified)

Becomes a thin host: its `Draw()` calls `_settingsPanel.DrawContent()`. The gear
icon (`OpenConfigUi`) keeps working with no duplicated fields. Keep the window’s
existing size/flags but allow it to fit the (slightly taller) unified content.

### 4. `Windows/MainWindow.cs` (modified)

Add a **"Settings"** tab (after Cleanup) that calls `_settingsPanel.DrawContent()`.
`MainWindow` receives the `SettingsPanel` via its constructor.

### 5. `Configuration.cs` (modified)

Add:
- `public VirtualKey HotkeyKey { get; set; } = VirtualKey.NO_KEY;`
- `public bool HotkeyCtrl { get; set; }`
- `public bool HotkeyAlt { get; set; }`
- `public bool HotkeyShift { get; set; }`

`VirtualKey` is `Dalamud.Game.ClientState.Keys.VirtualKey` (int-backed enum,
serializes fine with the existing Dalamud JSON config). Default `NO_KEY` =
unbound.

### 6. `Plugin.cs` (modified)

- Inject `IFramework framework`, `IKeyState keyState`, `IClientState clientState`.
- Construct `_hotkeyService = new HotkeyService(framework, keyState, Config, () => _mainWindow.Toggle())`
  **after** `_mainWindow` exists.
- Construct `_settingsPanel = new SettingsPanel(Config, _hotkeyService, clientState)`
  and pass it to `MainWindow` and `ConfigWindow`.
- `Dispose`: `_hotkeyService.Dispose()`.

## Data flow

```
each frame ─► IFramework.Update ─► HotkeyService:
                 ├─ capturing?  → scan IKeyState → record key+mods → Config.Save()
                 └─ else        → combo edge-pressed? → toggleMain() (= _mainWindow.Toggle())

Settings tab / gear window ─► SettingsPanel.DrawContent()
                 ├─ Set hotkey → hotkey.BeginCapture()
                 ├─ Clear      → unbind + save
                 └─ Save       → write Guild ID / API URL / overrides → Config.Save()

home world: SettingsPanel reads IClientState.LocalPlayer.HomeWorld → display + auto-fill Config.HomeWorld when empty
```

## Error handling / edge cases

- **Unbound by default** (`NO_KEY`) — no accidental conflicts.
- **Edge-triggered** toggle — one open/close per physical press.
- **Suppressed** while a plugin text field is focused (`WantTextInput`).
- **Capture** ignores modifier-only presses; `Esc` cancels; the new binding saves
  immediately.
- **Not logged in:** `IClientState.LocalPlayer` is null → show "Detected: —" and
  fall back to the manual override; no auto-fill, no crash.
- **Modifier-less binding** is allowed but the UI hints that a modifier is
  recommended (it can otherwise fire during normal play/chat, since we can't detect
  game-chat focus — only plugin text focus).

## Files

- **New:** `Services/HotkeyService.cs`, `Windows/SettingsPanel.cs`
- **Modified:** `Configuration.cs`, `Windows/ConfigWindow.cs`, `Windows/MainWindow.cs`,
  `Plugin.cs`, `QiqirnCompanion.json` (changelog/version at release time)

## Testing

Manual, in-game (no unit-test harness; compile gate is the automated check):

- Set a hotkey via capture (e.g. Ctrl+Q); press it → main window toggles open/closed.
- Press the hotkey while typing in the Search box → does **not** toggle.
- Clear the hotkey → pressing the old combo does nothing; label shows "Unbound".
- Reload the plugin → the hotkey persists.
- Settings tab and gear icon show the same unified content.
- Home World shows the detected world; an empty Home World auto-fills from the game.
- Logged out → "Detected: —", manual override still editable, no crash.

## Out of scope

- No rework of how trading presets consume `Config.HomeWorld` (they keep reading
  it; we just auto-populate it).
- No multi-key chord sequences (single combo = up to three modifiers + one key).
- No per-window hotkeys (one hotkey, toggles the main window).
