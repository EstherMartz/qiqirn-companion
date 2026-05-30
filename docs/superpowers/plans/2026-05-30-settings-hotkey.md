# Settings Tab + Open-Hotkey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a unified Settings tab and a user-configurable, press-to-bind hotkey that toggles the main window.

**Architecture:** A new `HotkeyService` owns all game-keyboard reading from an `IFramework.Update` tick (edge-triggered toggle + press-to-bind capture). A new `SettingsPanel.DrawContent()` renders the unified settings and is hosted by both the main window's new "Settings" tab and the existing gear Config window. Character + home world are read live from `IClientState`.

**Tech Stack:** C# / .NET 10, Dalamud API level 15 (`IFramework`, `IKeyState`, `IClientState`, `VirtualKey`/`GetFancyName`), Dalamud.Bindings.ImGui, Lumina (`World` sheet).

---

## Testing note (read first)

No unit-test harness exists (the plugin references the game's runtime DLLs; the codebase has zero tests; we are **not** adding one). Verification per task:

1. **Compile gate:** `cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && dotnet build -c Debug -clp:ErrorsOnly` — Spanish output; success = `Compilación correcta.` with `0 Errores`. This auto-deploys to `%AppData%\XIVLauncher\devPlugins\QiqirnCompanion`.
2. **Manual in-game checks** (listed at the end of the integration task).

Each task ends with the compile gate + a commit.

## Verified Dalamud API facts (used below — confirmed against dalamud.dev + Dalamud source)

- `VirtualKey` (namespace `Dalamud.Game.ClientState.Keys`): members `NO_KEY`, letters `A`..`Z`, `F1`..`F12`, modifiers `CONTROL`/`SHIFT`/`MENU` (+ `LCONTROL`/`RCONTROL`/`LSHIFT`/`RSHIFT`/`LMENU`/`RMENU`/`LWIN`/`RWIN`), `ESCAPE`.
- `VirtualKeyExtensions.GetFancyName(this VirtualKey)` → human-readable string (same namespace).
- `IKeyState` (`Dalamud.Plugin.Services`): `bool this[VirtualKey] { get; set; }`, `IEnumerable<VirtualKey> GetValidVirtualKeys()`.
- `IFramework` (`Dalamud.Plugin.Services`): `event ... Update` with handler `void (IFramework framework)`.
- `IClientState` (`Dalamud.Plugin.Services`): `IPlayerCharacter? LocalPlayer`. `LocalPlayer.Name.TextValue` (string). `LocalPlayer.HomeWorld` is `RowRef<World>`; world name via `.Value.Name.ToString()` (same idiom the repo uses for the Item sheet in `Services/SalesTracker.cs`).
- Dalamud services are injected by adding them as `Plugin` constructor parameters (same mechanism as the existing `IDataManager`/`IContextMenu`).

## File structure

- **New** `Services/HotkeyService.cs` — framework-tick keyboard reader: edge-triggered toggle + press-to-bind capture.
- **New** `Windows/SettingsPanel.cs` — `DrawContent()` unified settings UI (hotkey + connection + game-derived).
- **Modify** `Configuration.cs` — add hotkey fields.
- **Modify** `Windows/ConfigWindow.cs` — delegate to `SettingsPanel`.
- **Modify** `Windows/MainWindow.cs` — add "Settings" tab.
- **Modify** `Plugin.cs` — inject services, construct/wire/dispose.
- **Modify** `QiqirnCompanion.json` — changelog + version.

---

## Task 1: Configuration — hotkey fields

**Files:**
- Modify: `Configuration.cs`

- [ ] **Step 1: Add the using and fields**

In `Configuration.cs`, add this using near the existing `using` lines (after `using Dalamud.Plugin;`):

```csharp
using Dalamud.Game.ClientState.Keys;
```

Then add these four properties immediately after the `AutoLogRetainerSales` property (before the `// Injected by Plugin.cs after loading` region):

```csharp
    /// <summary>Hotkey that toggles the main window. NO_KEY = unbound.</summary>
    public VirtualKey HotkeyKey   { get; set; } = VirtualKey.NO_KEY;
    public bool       HotkeyCtrl  { get; set; }
    public bool       HotkeyAlt   { get; set; }
    public bool       HotkeyShift { get; set; }
```

- [ ] **Step 2: Compile gate**

Run: `cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && dotnet build -c Debug -clp:ErrorsOnly`
Expected: `0 Errores`.

- [ ] **Step 3: Commit**

```bash
cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && git add Configuration.cs && git commit -m "feat: add hotkey fields to Configuration"
```

---

## Task 2: HotkeyService

**Files:**
- Create: `Services/HotkeyService.cs`

- [ ] **Step 1: Create the service**

Create `Services/HotkeyService.cs` with this exact content:

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace QiqirnCompanion.Services;

/// <summary>
/// Owns all game-keyboard reading. Runs each framework tick: edge-detects the
/// user's configured combo to toggle the main window, and handles press-to-bind
/// capture when the Settings panel requests it. Dalamud has no hotkey registry,
/// so this polls <see cref="IKeyState"/> from <see cref="IFramework"/>.Update.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private static readonly HashSet<VirtualKey> Modifiers = new()
    {
        VirtualKey.CONTROL, VirtualKey.LCONTROL, VirtualKey.RCONTROL,
        VirtualKey.SHIFT,   VirtualKey.LSHIFT,   VirtualKey.RSHIFT,
        VirtualKey.MENU,    VirtualKey.LMENU,    VirtualKey.RMENU,
        VirtualKey.LWIN,    VirtualKey.RWIN,
    };

    private readonly IFramework    _framework;
    private readonly IKeyState     _keyState;
    private readonly Configuration _config;
    private readonly Action        _toggleMain;

    private bool _wasComboDown;

    public bool IsCapturing { get; private set; }

    public HotkeyService(IFramework framework, IKeyState keyState, Configuration config, Action toggleMain)
    {
        _framework  = framework;
        _keyState   = keyState;
        _config     = config;
        _toggleMain = toggleMain;
        _framework.Update += OnUpdate;
    }

    /// <summary>Enter listen mode; the next non-modifier key press becomes the binding.</summary>
    public void BeginCapture()  => IsCapturing = true;
    public void CancelCapture() => IsCapturing = false;

    private void OnUpdate(IFramework framework)
    {
        if (IsCapturing)
        {
            TryCapture();
            return;
        }

        if (_config.HotkeyKey == VirtualKey.NO_KEY || ImGui.GetIO().WantTextInput)
        {
            _wasComboDown = false;
            return;
        }

        var down = _keyState[_config.HotkeyKey]
            && (!_config.HotkeyCtrl  || _keyState[VirtualKey.CONTROL])
            && (!_config.HotkeyAlt   || _keyState[VirtualKey.MENU])
            && (!_config.HotkeyShift || _keyState[VirtualKey.SHIFT]);

        if (down && !_wasComboDown)
            _toggleMain();
        _wasComboDown = down;
    }

    private void TryCapture()
    {
        if (_keyState[VirtualKey.ESCAPE])
        {
            IsCapturing = false;
            return;
        }

        foreach (var vk in _keyState.GetValidVirtualKeys())
        {
            if (Modifiers.Contains(vk) || vk == VirtualKey.ESCAPE) continue;
            if (!_keyState[vk]) continue;

            _config.HotkeyKey   = vk;
            _config.HotkeyCtrl  = _keyState[VirtualKey.CONTROL];
            _config.HotkeyAlt   = _keyState[VirtualKey.MENU];
            _config.HotkeyShift = _keyState[VirtualKey.SHIFT];
            _config.Save();
            IsCapturing = false;
            return;
        }
    }

    public void Dispose() => _framework.Update -= OnUpdate;
}
```

- [ ] **Step 2: Compile gate**

Run: `cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && dotnet build -c Debug -clp:ErrorsOnly`
Expected: `0 Errores`. (Class compiles standalone; not yet wired.) If a member is rejected (e.g. `VirtualKey.ESCAPE` or `ImGui.GetIO().WantTextInput`), read the compiler error and correct against the local SDK; if you can't, report BLOCKED with the verbatim error.

- [ ] **Step 3: Commit**

```bash
cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && git add Services/HotkeyService.cs && git commit -m "feat: add HotkeyService (framework-tick toggle + press-to-bind capture)"
```

---

## Task 3: SettingsPanel

**Files:**
- Create: `Windows/SettingsPanel.cs`

- [ ] **Step 1: Create the panel**

Create `Windows/SettingsPanel.cs` with this exact content:

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using QiqirnCompanion.Services;
using System.Numerics;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Unified settings UI rendered both as the main window's "Settings" tab and
/// inside the gear-icon Config window. Owns its edit buffers; the Save button
/// persists the text fields, while the hotkey and home-world auto-fill save
/// immediately when they change.
/// </summary>
public class SettingsPanel
{
    private readonly Configuration _config;
    private readonly HotkeyService _hotkey;
    private readonly IClientState  _clientState;

    private string _guildIdBuf;
    private string _apiBaseUrlBuf;
    private string _charOverrideBuf;
    private string _homeWorldBuf;

    public SettingsPanel(Configuration config, HotkeyService hotkey, IClientState clientState)
    {
        _config      = config;
        _hotkey      = hotkey;
        _clientState = clientState;
        _guildIdBuf      = config.GuildId;
        _apiBaseUrlBuf   = config.ApiBaseUrl;
        _charOverrideBuf = config.CharacterNameOverride;
        _homeWorldBuf    = config.HomeWorld;
    }

    private string BindingLabel()
    {
        if (_config.HotkeyKey == VirtualKey.NO_KEY) return "Unbound";
        var s = "";
        if (_config.HotkeyCtrl)  s += "Ctrl + ";
        if (_config.HotkeyAlt)   s += "Alt + ";
        if (_config.HotkeyShift) s += "Shift + ";
        return s + _config.HotkeyKey.GetFancyName();
    }

    private string? DetectedWorld()
    {
        var p = _clientState.LocalPlayer;
        return p is null ? null : p.HomeWorld.Value.Name.ToString();
    }

    public void DrawContent()
    {
        // ── Hotkey ────────────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.5f, 0.85f, 1f, 1), "Open hotkey");
        ImGui.TextUnformatted($"Current: {BindingLabel()}");

        if (_hotkey.IsCapturing)
        {
            ImGui.TextColored(new Vector4(1, 0.9f, 0.4f, 1), "Press a key…  (Esc to cancel)");
            if (ImGui.Button("Cancel##hk")) _hotkey.CancelCapture();
        }
        else
        {
            if (ImGui.Button("Set hotkey")) _hotkey.BeginCapture();
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                _config.HotkeyKey  = VirtualKey.NO_KEY;
                _config.HotkeyCtrl = _config.HotkeyAlt = _config.HotkeyShift = false;
                _config.Save();
            }
        }
        ImGui.TextDisabled("Toggles the Qiqirn window. A modifier (Ctrl/Alt/Shift) is recommended.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Connection ────────────────────────────────────────────────────────
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Guild ID##gid", ref _guildIdBuf, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your Discord server (guild) ID.\nRight-click server icon in Discord → Copy Server ID.");

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("API URL##url", ref _apiBaseUrlBuf, 128);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leave as https://qiqirn.tools unless you are self-hosting.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Game-derived (character + home world) ──────────────────────────────
        var p             = _clientState.LocalPlayer;
        var detectedName  = p?.Name.TextValue;
        var detectedWorld = DetectedWorld();

        ImGui.TextUnformatted($"Detected character: {detectedName ?? "—"}");
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Character override##char", ref _charOverrideBuf, 64);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leave blank to use the detected character name.");

        ImGui.Spacing();

        ImGui.TextUnformatted($"Detected home world: {detectedWorld ?? "—"}");
        // Auto-fill home world from the game when the user hasn't set one.
        if (string.IsNullOrWhiteSpace(_homeWorldBuf) && detectedWorld != null)
        {
            _homeWorldBuf = detectedWorld;
            if (_config.HomeWorld != detectedWorld)
            {
                _config.HomeWorld = detectedWorld;
                _config.Save();
            }
        }
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Home World override##world", ref _homeWorldBuf, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Auto-filled from the game. Override for logged-out / DC-travel use.\nRequired for home-scope trading presets.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Save"))
        {
            _config.GuildId               = _guildIdBuf.Trim();
            _config.ApiBaseUrl            = string.IsNullOrWhiteSpace(_apiBaseUrlBuf)
                                              ? "https://qiqirn.tools"
                                              : _apiBaseUrlBuf.Trim();
            _config.CharacterNameOverride = _charOverrideBuf.Trim();
            _config.HomeWorld             = _homeWorldBuf.Trim();
            _config.Save();
        }
    }
}
```

- [ ] **Step 2: Compile gate**

Run: `cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && dotnet build -c Debug -clp:ErrorsOnly`
Expected: `0 Errores`. (Compiles standalone; not yet wired.) If `p.HomeWorld.Value.Name.ToString()` or `p.Name.TextValue` is rejected, correct against the local SDK (e.g. `.HomeWorld.ValueNullable?.Name` or `.Name.ToString()`); if stuck, report BLOCKED with the error.

- [ ] **Step 3: Commit**

```bash
cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && git add Windows/SettingsPanel.cs && git commit -m "feat: add SettingsPanel (unified settings + hotkey UI)"
```

---

## Task 4: Integration — Plugin + ConfigWindow + MainWindow

Wire the new service + panel in. These three files change together so the build stays green.

**Files:**
- Modify: `Windows/ConfigWindow.cs`
- Modify: `Windows/MainWindow.cs`
- Modify: `Plugin.cs`

- [ ] **Step 1: Make `ConfigWindow` host the panel**

Replace the entire contents of `Windows/ConfigWindow.cs` with:

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace QiqirnCompanion.Windows;

public class ConfigWindow : Window
{
    private readonly SettingsPanel _settings;

    public ConfigWindow(SettingsPanel settings)
        : base("Qiqirn Companion — Config##config", ImGuiWindowFlags.NoScrollbar)
    {
        _settings = settings;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 360),
            MaximumSize = new Vector2(600, 800),
        };
    }

    public override void Draw() => _settings.DrawContent();
}
```

- [ ] **Step 2: Add the "Settings" tab to `MainWindow`**

In `Windows/MainWindow.cs`, add a field next to the other window fields (after `private readonly CleanupWindow _cleanupWindow;`):

```csharp
    private readonly SettingsPanel  _settingsPanel;
```

Add a `SettingsPanel settingsPanel` parameter to the constructor (append it to the parameter list) and assign it. The constructor signature becomes:

```csharp
    public MainWindow(Configuration config, ApiClient api, IPlayerState playerState, SearchWindow searchWindow, TradingWindow tradingWindow, PlannerWindow plannerWindow, CleanupWindow cleanupWindow, SettingsPanel settingsPanel)
```

and add inside the constructor body (next to the other assignments):

```csharp
        _settingsPanel = settingsPanel;
```

In `Draw()`, add the Settings tab call after `DrawCleanupTab();`:

```csharp
        DrawCleanupTab();
        DrawSettingsTab();
```

Add this method (e.g. right after `DrawCleanupTab`):

```csharp
    private void DrawSettingsTab()
    {
        if (!ImGui.BeginTabItem("Settings")) return;
        _settingsPanel.DrawContent();
        ImGui.EndTabItem();
    }
```

- [ ] **Step 3: Wire `Plugin.cs`**

In `Plugin.cs`:

(a) Add fields next to the other window/service fields (after `private readonly ContextMenuService _contextMenuService;`):

```csharp
    private readonly HotkeyService  _hotkeyService;
    private readonly SettingsPanel  _settingsPanel;
```

(b) Add the three services to the constructor parameter list (after `IContextMenu contextMenu`):

```csharp
        IContextMenu            contextMenu,
        IFramework              framework,
        IKeyState               keyState,
        IClientState            clientState)
```

(c) In the `// Windows` region, construct the hotkey service + settings panel **before** `_mainWindow` and `_configWindow`, and pass the panel into both. Replace the existing Windows block so it reads:

```csharp
        // Hotkey service + unified settings panel (constructed before the windows
        // that consume the panel). The toggle lambda reads _mainWindow lazily.
        _hotkeyService = new HotkeyService(framework, keyState, Config, () => _mainWindow.Toggle());
        _settingsPanel = new SettingsPanel(Config, _hotkeyService, clientState);

        // Windows
        _itemInfoWindow = new ItemInfoWindow(_api);
        _searchWindow  = new SearchWindow(_api, _itemInfoWindow);
        _tradingWindow = new TradingWindow(_api);
        _plannerWindow = new PlannerWindow(Config, _salesTracker);
        _cleanupWindow = new CleanupWindow(_api);
        _mainWindow    = new MainWindow(Config, _api, playerState, _searchWindow, _tradingWindow, _plannerWindow, _cleanupWindow, _settingsPanel);
        _configWindow  = new ConfigWindow(_settingsPanel);
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);
        _windowSystem.AddWindow(_searchWindow);
        _windowSystem.AddWindow(_itemInfoWindow);
        _windowSystem.AddWindow(_tradingWindow);
        _windowSystem.AddWindow(_plannerWindow);
        _windowSystem.AddWindow(_cleanupWindow);
```

(d) In `Dispose()`, add (after `_contextMenuService.Dispose();`):

```csharp
        _hotkeyService.Dispose();
```

- [ ] **Step 4: Compile gate**

Run: `cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && dotnet build -c Debug -clp:ErrorsOnly`
Expected: `0 Errores`. (`IFramework`/`IKeyState`/`IClientState` are in `Dalamud.Plugin.Services`, already imported by `Plugin.cs`.)

- [ ] **Step 5: Commit**

```bash
cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && git add Plugin.cs Windows/ConfigWindow.cs Windows/MainWindow.cs && git commit -m "feat: Settings tab + gear window host SettingsPanel; wire HotkeyService"
```

- [ ] **Step 6: Manual in-game checks** (after `/xldev` → Reload)

- Open the main window → **Settings** tab present; gear icon opens the same content.
- Click **Set hotkey** → "Press a key…" → press `Ctrl+Q` → label shows `Ctrl + Q`.
- Press `Ctrl+Q` in-game → main window toggles open/closed.
- Press `Ctrl+Q` while the cursor is in the Search box → does **not** toggle.
- **Clear** → label shows `Unbound`; old combo no longer toggles.
- "Detected home world" shows your world; with Home World blank it auto-fills.
- Reload the plugin → the hotkey persists.

---

## Task 5: Manifest changelog + version bump + release build

**Files:**
- Modify: `QiqirnCompanion.json`

- [ ] **Step 1: Update the manifest**

In `QiqirnCompanion.json`, set:

```json
  "Changelog": "Settings tab: bind a hotkey to toggle the window (press-to-bind), with all settings unified in one place; character and home world are now auto-detected from the game.",
```
```json
  "AssemblyVersion": "1.4.0.0",
```

(The published version is governed by the release tag/CI; this keeps the in-repo manifest consistent. Do not run the release here.)

- [ ] **Step 2: Release build (packaging gate)**

Run: `cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && dotnet build -c Release -clp:ErrorsOnly`
Expected: `0 Errores`; `latest.zip` produced under `bin/Release/net10.0-windows/`.

- [ ] **Step 3: Commit**

```bash
cd "C:/Users/esthe/Documents/Dev/qiqirn-companion" && git add QiqirnCompanion.json && git commit -m "chore: changelog + version bump for settings + hotkey"
```

---

## Out of scope

- No rework of how trading presets read `Config.HomeWorld` (they keep reading it; we auto-populate it).
- No multi-key chord sequences; one combo (≤3 modifiers + one key).
- No per-window hotkeys (single hotkey → toggles the main window).
