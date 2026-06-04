# Item view: header, verdict, and priced recipe

Date: 2026-06-04

## Goal

Bring the top of the web app's individual item view
([`ffxiv-helper/src/routes/Item.tsx`](../../../../ffxiv-helper/src/routes/Item.tsx))
into the plugin's individual item view
([`Windows/ItemInfoWindow.cs`](../../../Windows/ItemInfoWindow.cs)). This is the
first slice of a broader effort to bring app features to the plugin.

Three blocks are in scope:

1. **Header** — item name in rarity color; chips for Item Level, category,
   rarity tier, and HQ-capable; external link buttons (Garland Tools, Gamer
   Escape, Universalis).
2. **Verdict card** — the signature "make vs buy" call-out: a toned headline,
   rationale, best play, and margin/risk, computed server-side by the existing
   `computeVerdict`.
3. **Priced recipe table** — each ingredient shows qty, home unit price,
   subtotal, and a source tag (Vendor / Gather / Craft / MB), plus a material
   total row. Replaces the current bullet list in `DrawRecipeSource`.

### Out of scope (later slices)

- The "sell as" stack suggestion (needs price history).
- Craft→sell math card, region material-shopping, craft tree, used-in,
  deliverables blocks.
- Per-world / DC / region market detail beyond the existing one-line summary.

## Architecture

The verdict logic (`computeVerdict` + `plays` + `pricing`, ~290 lines of tested
TypeScript) and all required data already live in the backend repo. Rather than
port that logic to C#, the plugin stays a thin renderer: the backend
`api/plugin-item-sources` endpoint is extended to attach the extra data and to
run `computeVerdict`, returning a ready-to-render result. This reuses tested
logic and keeps the plugin verdict in sync with the web app.

```
ItemInfoWindow.Show(itemId)
  → ApiClient.GetItemSourcesAsync(itemId)
    → GET api/plugin/item-sources?id=...
      backend: loadSnapshots + market cache (already loaded)
        • item meta (ilvl, category, rarity, canHq)
        • recipe ingredient prices + source tags + material total
        • computeVerdict(...) → flattened verdict (+ runnerUp)
  → render: header chips/links · verdict card · market summary · priced recipe · other sources
```

## Backend changes — `ffxiv-helper/src/api/plugin-item-sources.ts`

All inputs are already loaded in the handler (`cache.phantom/dc/region`,
`snapshots.itemsById`, `vendorMap`, `gatheringCatalog`, `recipes`). Add to the
JSON response:

### Item metadata (top level)

From `snapshots.itemsById.get(itemId)` (a `SnapshotItem` with `ilvl`, `sc`,
`rarity`, `canHq`):

- `ilvl: number`
- `category: string` — via `categoryLabel(item.sc)` from
  `lib/itemSearchCategories`. Returning the label string avoids porting the
  category map to C#.
- `rarity: number`
- `canHq: boolean`

### Recipe enrichment (per `RecipeSource`)

- Each ingredient gains:
  - `unitPrice: number | null` — `cache.phantom[ingId]?.minNQ ?? minHQ ?? null`.
  - `source: 'vendor' | 'gather' | 'craft' | 'mb'` — classified in priority
    order: in `vendorMap` → `vendor`; else in `gatheringCatalog` → `gather`;
    else has a recipe in `recipes` → `craft`; else → `mb`.
- The `RecipeSource` gains `materialCost: number` — `Σ (unitPrice ?? 0) × qty`
  over its ingredients (home prices).

### Verdict (top level, nullable)

Build a `VerdictInput` from the **primary** recipe (first recipe source, if any)
and call the existing `computeVerdict`:

- `phantom = cache.phantom[itemId]`, `region = cache.region[itemId]`
- `recipe` — mapped to the `lib/recipes` `Recipe` shape needed by the plays
  (`classJob` name, `recipeLevel` number, `ingredients`); only used for the
  `bestPlayDetail` string, so an approximate mapping is acceptable.
- `vendorPrice = vendorMap.get(itemId)`
- `materialCost` — the primary recipe's material total (above)
- `homeWorld` — the backend phantom-world label constant
- `canHq` — item meta above
- `now = Date.now()`

Return a flattened `verdict` object with the fields the card renders:
`headline, rationale, bestPlay, bestPlayDetail, netPerUnit, gilPerDay, roi,
risk, tone, quality, kind`, and a nullable `runnerUp` (`bestPlay, gilPerDay,
kind`). When there is no usable home price, `computeVerdict` returns its
`untraded` result; that is passed through and rendered as a "Not enough data"
state. When the item has no recipe and no usable market data, `verdict` may be
omitted/null and the card is hidden.

### Implementation-time details (flagged, non-blocking)

- Source the `homeWorld` label (phantom-world constant) used by `arbPlay` and
  rationale strings.
- Confirm `categoryLabel` and the verdict modules import cleanly in the
  serverless/node handler context.

## Plugin changes

### `Services/ApiClient.cs` — DTOs

- `IngredientItem`: add `UnitPrice` (`int?`) and `Source` (`string`).
- `RecipeSource`: add `MaterialCost` (`int`).
- `ItemSourcesResponse`: add `Ilvl` (`int`), `Category` (`string?`), `Rarity`
  (`int`), `CanHq` (`bool`), `Verdict` (nullable record), `RunnerUp` (nullable
  record). New records `Verdict` and `VerdictRunnerUp` with the fields above,
  using `JsonPropertyName` to match the backend keys.

### `Services/ItemInteractions.cs` — reuse links

Extract the external-site URL patterns currently inline in `DrawContextMenu`
into small reusable helpers (e.g. `OpenGarland(itemId)`,
`OpenUniversalis(itemId)`, `OpenGamerEscape(name)`) so the header buttons and
the context menu share one source of truth.

### `Windows/ItemInfoWindow.cs` — rendering

- **Rarity color helper**: port the 5-case mapping from
  `features/items/rarity.ts` to an ImGui `Vector4` (tiers 2/3/4/7 → color; else
  default) plus `rarityLabel`.
- **Header**: render the item name in its rarity color; a chip row for
  `Item Lvl {ilvl}` (when > 1), `{category}`, rarity tier, and `HQ-Capable`
  (when `canHq`); a row of link buttons calling the `ItemInteractions` helpers.
- **Verdict card**: a `tone → Vector4` map; render headline (toned),
  rationale (wrapped), best play + detail, margin (`+net/unit`, `~gil/day`,
  `ROI%` when present), and risk; a runner-up line when present. Render nothing
  when `Verdict` is null.
- **Recipe table**: replace the ingredient bullet list with an ImGui table —
  columns Ingredient | Qty | Unit | Subtotal | Source — and a material-total
  row. Ingredient rows keep the existing item interactions (copy / link /
  context menu) via `ItemInteractions.HandleRow`.

Window order matches the web view: header → verdict → market summary → priced
recipe → other sources.

## Testing

- **Backend**: unit-test the new ingredient `source` classifier (vendor /
  gather / craft / mb priority) and the enriched response shape. The verdict
  computation itself is already covered by existing tests.
- **Plugin**: project builds cleanly. Manual verification in-game against:
  - a known HQ-capable crafted item (header chips, verdict with a craft play,
    priced recipe with mixed source tags), and
  - an untraded / non-craftable item (verdict shows "Not enough data" or is
    hidden; recipe table absent).
