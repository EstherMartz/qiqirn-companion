# Ruleta del Estilismo — Design

**Date:** 2026-06-05
**Status:** Approved (pending spec review)

## Summary

A hidden "easter egg" window for the guild's weekly *Ruleta del Estilismo*
event. The MC opens it with `/qiqirn ruleta`, enters a person's name and their
total number of available haircuts, and clicks a button to randomly roll a
styling assignment: one haircut, a base hair color (grid row + column), and a
highlight color (grid row + column). The plugin formats a Spanish summary line
and copies it to the clipboard so the MC can paste it into whatever chat channel
they like.

The hair-color grid in the in-game appearance menu is **8 columns × 24 rows**;
both base color and highlights are picked from that same grid.

## Goals

- Let the MC roll a full styling assignment with one click.
- Produce a clean, paste-ready Spanish message.
- Keep the feature hidden — reachable only via `/qiqirn ruleta`, with no
  menu button or other visible entry point.

## Non-Goals (YAGNI)

- No auto-sending to public chat (clipboard only — see Account Safety).
- No session history of past rolls.
- No per-field re-roll/override UI. (Clicking the roll button again simply
  rolls fresh; that is the only "re-roll".)
- No race/haircut game-data lookups. The haircut count is fully MC-entered,
  since it varies by race and by unlocked content.
- No backend/API involvement. The feature is entirely local.

## Account Safety

The plugin has an explicit, stated philosophy (`Services/GameActions.cs`):
*"All actions are local to the player — nothing is ever sent to a public chat
channel."* This feature honors that. The result reaches other players **only**
by the MC manually pasting clipboard text into chat and pressing Enter — the
same sanctioned `ImGui.SetClipboardText` path already used elsewhere in the
plugin. We deliberately do **not** programmatically send chat (which would
require an unsanctioned signature and carry account-action risk).

## Architecture

One new window, one small pure helper, and two small edits to `Plugin.cs`. No
new services, no API client work, no game-data access.

```
/qiqirn ruleta ──► Plugin.OnCommand (special-cased)
                      │
                      ▼
              RuletaWindow (ImGui)
                      │  on "Daleee" click
                      ▼
              RuletaRoll.Roll(count, rng)  ── pure
                      │
                      ▼
              RuletaRoll.Format(name, roll) ── pure ──► clipboard
```

## Components

### `Windows/RuletaRoll.cs` (new)

Pure logic, no ImGui — the one piece worth isolating and keeping trivially
correct.

- `readonly record struct RuletaRoll(int Cut, int Count, int BaseRow, int BaseCol, int HiRow, int HiCol)`
- `static RuletaRoll Roll(int count, Random rng)`:
  - `Cut   = rng.Next(1, count + 1)`   → `1..count`
  - `BaseRow = rng.Next(1, 25)`         → `1..24`
  - `BaseCol = rng.Next(1, 9)`          → `1..8`
  - `HiRow = rng.Next(1, 25)`           → `1..24`
  - `HiCol = rng.Next(1, 9)`            → `1..8`
  - `Count = count` (carried so the message can show `Cut/Count`)
- `static string Format(string name, RuletaRoll r)` → see Message Format.

Constants for the grid bounds (`Columns = 8`, `Rows = 24`) live here.

### `Windows/RuletaWindow.cs` (new)

A `Window` subclass, same shape as `SearchWindow`. State:

- `string _name = ""`
- `int _count = 0`
- `RuletaRoll? _result = null`
- `string _message = ""`  (regenerated whenever name or roll changes)
- A single shared `Random _rng = new()`

Draw:

1. `InputText` for the name (`Nombre`).
2. `InputInt` for the haircut count (`Cortes disponibles`).
3. **"Daleee"** button — disabled while `_count < 1`. On click:
   `_result = RuletaRoll.Roll(_count, _rng); _message = RuletaRoll.Format(_name, _result.Value);`
4. When `_result` has a value: show the message as read-only/wrapped text
   (the preview) and a **"Copiar"** button → `ImGui.SetClipboardText(_message)`.
   - If the name is edited after rolling, regenerate `_message` so the preview
     stays in sync.

Window title: `Ruleta del Estilismo`. Reasonable min size; not resizable-critical.

### `Plugin.cs` (edit)

- Construct `_ruletaWindow = new RuletaWindow();`
- `_windowSystem.AddWindow(_ruletaWindow);`
- Do **not** add it to `MainWindow` / any button — it stays hidden.
- In `OnCommand`, before the search path:

  ```csharp
  if (query.Equals("ruleta", StringComparison.OrdinalIgnoreCase))
  {
      _ruletaWindow.Toggle();
      return;
  }
  ```

  (Required: otherwise `/qiqirn ruleta` runs an item search for "ruleta".)
- No explicit dispose needed beyond `_windowSystem.RemoveAllWindows()` already
  in `Dispose`, since `RuletaWindow` holds no unmanaged/IDisposable resources.
  (Implement `IDisposable` with an empty body for consistency with the other
  windows, e.g. `SearchWindow`.)

## Message Format

```
Ruleta del Estilismo - [Nombre] | Corte: 27/73 | Color base: F12 C1 | Mechas: F1 C5
```

- `[Nombre]` — the entered name (may be blank).
- `Corte: 27/73` — rolled haircut number out of the entered total.
- `Color base: F12 C1` — base color, **F**ila (row) 12, **C**olumna (column) 1.
- `Mechas: F1 C5` — highlights, row 1, column 5.

**Character safety:** ASCII only (`-`, `|`, `/`, digits, letters). FFXIV's chat
input filters some Unicode (em-dash, middot, boxed glyphs); none of the Spanish
words used here need accents, so the message pastes cleanly into any channel.

## Data Flow

1. MC: `/qiqirn ruleta` → window toggles open.
2. MC enters `Nombre` and `Cortes disponibles`.
3. MC clicks **Daleee** → `RuletaRoll.Roll` fills `_result`, message built.
4. Preview shows the line; MC clicks **Copiar** → clipboard set.
5. MC pastes into `/fc`, `/say`, `/p`, etc., and presses Enter.

## Edge Cases

- **Count < 1 or empty:** "Daleee" disabled — cannot roll a cut out of zero.
- **Empty name:** allowed; message shows an empty name slot (minimal, not
  blocking).
- **Re-clicking Daleee:** rolls fresh and overwrites the previous result. This
  is the only re-roll mechanism by design.

## Testing

Dalamud plugins run inside the game; this repo has no automated test harness,
and adding one is out of scope. `RuletaRoll.Roll`/`Format` are pure and
verifiable by inspection. Manual verification in-game:

- `/qiqirn ruleta` opens the window; `/qiqirn ruleta` again closes it.
- `/qiqirn <other text>` still runs an item search (regression check).
- Rolls land in range: cut `1..count`, rows `1..24`, cols `1..8`.
- **Daleee** is disabled at count 0 and enabled at count ≥ 1.
- **Copiar** puts the exact previewed line on the clipboard; it pastes cleanly
  into FFXIV chat.
