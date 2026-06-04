# Item view: header, verdict, priced recipe — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the top of the web app's item view — header chips + external links, the make-vs-buy verdict, and a priced recipe table with source tags — into the plugin's `ItemInfoWindow`.

**Architecture:** The backend `api/plugin-item-sources` endpoint (in the sibling `ffxiv-helper` repo) is extended to attach item metadata, per-ingredient prices + source tags + a material total, and a server-computed verdict (reusing the existing tested `computeVerdict`). The plugin (`qiqirn-companion`) stays a thin renderer that deserializes the richer response and draws it with ImGui.

**Tech Stack:** Backend — TypeScript, vitest. Plugin — C# / .NET 10, Dalamud ImGui bindings.

**Two repos:**
- Backend: `C:\Users\esthe\Documents\Dev\ffxiv-helper` (Tasks 1–3)
- Plugin: `C:\Users\esthe\Documents\Dev\qiqirn-companion` (Tasks 4–8, current repo, branch `feat/item-view-header-verdict-recipe`)

**Spec:** [docs/superpowers/specs/2026-06-04-item-view-header-verdict-recipe-design.md](../specs/2026-06-04-item-view-header-verdict-recipe-design.md)

---

## File Structure

**Backend (`ffxiv-helper`):**
- Create: `src/api/_item-sources-core.ts` — pure helpers (source classifier, crafter-code→name, recipe pricing). `_`-prefixed so it is not registered as an HTTP route.
- Create: `src/api/_item-sources-core.test.ts` — vitest unit tests for the helpers.
- Modify: `src/api/plugin-item-sources.ts` — use the helpers; add item meta + verdict to the response.

**Plugin (`qiqirn-companion`):**
- Modify: `Services/ApiClient.cs` — extend DTOs (`IngredientItem`, `RecipeSource`, `ItemSourcesResponse`) + new `Verdict` / `VerdictRunnerUp` records.
- Modify: `Services/ItemInteractions.cs` — extract reusable external-link helpers.
- Modify: `Windows/ItemInfoWindow.cs` — rarity color/label helpers; header block; verdict card; priced recipe table.

---

## Task 1: Backend pure helpers (`_item-sources-core.ts`)

**Files:**
- Create: `C:\Users\esthe\Documents\Dev\ffxiv-helper\src\api\_item-sources-core.ts`
- Test: `C:\Users\esthe\Documents\Dev\ffxiv-helper\src\api\_item-sources-core.test.ts`

First create a branch in the backend repo.

- [ ] **Step 1: Create the backend branch**

```bash
cd /c/Users/esthe/Documents/Dev/ffxiv-helper
git checkout -b feat/plugin-item-view-data
```

- [ ] **Step 2: Write the failing test**

Create `src/api/_item-sources-core.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import type { BotSnapshots } from '../bot/loadSnapshots';
import type { MarketData } from '../lib/universalis';
import type { Recipe } from '../lib/recipes';
import { classifyIngredientSource, jobNameOf, priceRecipe } from './_item-sources-core';

function fakeSnapshots(over: Partial<BotSnapshots> = {}): BotSnapshots {
  return {
    itemsById: new Map(),
    namesById: new Map<number, string>([[5106, 'Bronze Ingot'], [2, 'Fire Shard']]),
    recipes: new Map(),
    vendorMap: new Map<number, number>(),
    specialShop: { byCurrency: new Map() },
    gatheringCatalog: new Map(),
    companyCraft: new Map(),
    ...over,
  } as BotSnapshots;
}

describe('classifyIngredientSource', () => {
  it('prefers vendor, then gather, then craft, else mb', () => {
    const snaps = fakeSnapshots({
      vendorMap: new Map([[10, 5]]),
      gatheringCatalog: new Map([[20, { level: 1, timed: false } as any]]),
      recipes: new Map([[30, {} as Recipe]]),
    });
    expect(classifyIngredientSource(10, snaps)).toBe('vendor');
    expect(classifyIngredientSource(20, snaps)).toBe('gather');
    expect(classifyIngredientSource(30, snaps)).toBe('craft');
    expect(classifyIngredientSource(99, snaps)).toBe('mb');
  });

  it('vendor wins even when the item is also gatherable', () => {
    const snaps = fakeSnapshots({
      vendorMap: new Map([[10, 5]]),
      gatheringCatalog: new Map([[10, { level: 1, timed: false } as any]]),
    });
    expect(classifyIngredientSource(10, snaps)).toBe('vendor');
  });
});

describe('jobNameOf', () => {
  it('maps known crafter codes and falls back to the raw code', () => {
    expect(jobNameOf('CRP')).toBe('Carpenter');
    expect(jobNameOf('ALC')).toBe('Alchemist');
    expect(jobNameOf('ANY')).toBe('Any Crafter');
    expect(jobNameOf('XYZ')).toBe('XYZ');
  });
});

describe('priceRecipe', () => {
  const recipe: Recipe = {
    itemResultId: 5056,
    classJob: 'BSM',
    recipeLevel: 1,
    ingredients: [
      { itemId: 5106, amount: 2 },
      { itemId: 2, amount: 1 },
    ],
    amountResult: 1,
  };

  it('prices ingredients (minNQ → minHQ → null) and sums the material cost', () => {
    const phantom = {
      '5106': { minNQ: 100, minHQ: 150 },
      // 2 has no NQ price, falls back to HQ
      '2': { minNQ: null, minHQ: 7 },
    } as unknown as MarketData;
    const snaps = fakeSnapshots({ vendorMap: new Map([[2, 1]]) });

    const out = priceRecipe(recipe, phantom, snaps);

    expect(out.ingredients[0]).toMatchObject({
      itemId: 5106, itemName: 'Bronze Ingot', qty: 2, unitPrice: 100, source: 'mb',
    });
    expect(out.ingredients[1]).toMatchObject({
      itemId: 2, itemName: 'Fire Shard', qty: 1, unitPrice: 7, source: 'vendor',
    });
    // 100*2 + 7*1
    expect(out.materialCost).toBe(207);
  });

  it('treats missing market entries as 0 cost with null unitPrice', () => {
    const out = priceRecipe(recipe, {} as MarketData, fakeSnapshots());
    expect(out.ingredients[0].unitPrice).toBeNull();
    expect(out.materialCost).toBe(0);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd /c/Users/esthe/Documents/Dev/ffxiv-helper && npx vitest run src/api/_item-sources-core.test.ts`
Expected: FAIL — cannot find module `./_item-sources-core`.

- [ ] **Step 4: Write the implementation**

Create `src/api/_item-sources-core.ts`:

```ts
import type { BotSnapshots } from '../bot/loadSnapshots';
import type { MarketData } from '../lib/universalis';
import type { Recipe } from '../lib/recipes';

export type IngredientSourceTag = 'vendor' | 'gather' | 'craft' | 'mb';

/**
 * Classify how an ingredient is most cheaply obtained, in priority order:
 * NPC vendor → gatherable → craftable → marketboard-only. Mirrors the web
 * app's per-ingredient source tag, derived from the same snapshots.
 */
export function classifyIngredientSource(itemId: number, snapshots: BotSnapshots): IngredientSourceTag {
  if (snapshots.vendorMap.has(itemId)) return 'vendor';
  if (snapshots.gatheringCatalog.has(itemId)) return 'gather';
  if (snapshots.recipes.has(itemId)) return 'craft';
  return 'mb';
}

const JOB_NAME_BY_CODE: Record<string, string> = {
  CRP: 'Carpenter', BSM: 'Blacksmith', ARM: 'Armorer', GSM: 'Goldsmith',
  LTW: 'Leatherworker', WVR: 'Weaver', ALC: 'Alchemist', CUL: 'Culinarian',
  ANY: 'Any Crafter',
};

/** Map a crafter code (e.g. 'CRP') to a display job name, falling back to the code. */
export function jobNameOf(code: string): string {
  return JOB_NAME_BY_CODE[code] ?? code;
}

export interface PricedIngredient {
  itemId: number;
  itemName: string;
  qty: number;
  unitPrice: number | null;
  source: IngredientSourceTag;
}

export interface PricedRecipe {
  ingredients: PricedIngredient[];
  materialCost: number;
}

/**
 * Attach a home (phantom) unit price and source tag to each ingredient and sum
 * the material cost. A missing price contributes 0 to the total and a null
 * unitPrice (so the UI can show "—").
 */
export function priceRecipe(recipe: Recipe, phantom: MarketData, snapshots: BotSnapshots): PricedRecipe {
  let materialCost = 0;
  const ingredients: PricedIngredient[] = recipe.ingredients.map((ing) => {
    const m = phantom[String(ing.itemId)];
    const unitPrice = m?.minNQ ?? m?.minHQ ?? null;
    materialCost += (unitPrice ?? 0) * ing.amount;
    return {
      itemId: ing.itemId,
      itemName: snapshots.namesById.get(ing.itemId) ?? `Item #${ing.itemId}`,
      qty: ing.amount,
      unitPrice,
      source: classifyIngredientSource(ing.itemId, snapshots),
    };
  });
  return { ingredients, materialCost };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd /c/Users/esthe/Documents/Dev/ffxiv-helper && npx vitest run src/api/_item-sources-core.test.ts`
Expected: PASS (3 describe blocks, all green).

- [ ] **Step 6: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/ffxiv-helper
git add src/api/_item-sources-core.ts src/api/_item-sources-core.test.ts
git commit -m "feat(api): item-sources helpers — source tags, job names, recipe pricing"
```

---

## Task 2: Wire helpers + item metadata into the endpoint

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\ffxiv-helper\src\api\plugin-item-sources.ts`

This replaces the buggy `recipe.classJobId` / `recipe.recipeLevel?.stars` reads (those fields don't exist on the snapshot recipe, which is `{ classJob: CrafterCode, recipeLevel: number, stats: { stars } }`) and adds priced ingredients + item meta.

- [ ] **Step 1: Add imports**

At the top of `src/api/plugin-item-sources.ts`, alongside the existing imports, add:

```ts
import { priceRecipe, jobNameOf } from './_item-sources-core';
import { categoryLabel } from '../lib/itemSearchCategories';
```

- [ ] **Step 2: Capture the priced market cache once and the primary recipe**

The handler already calls `loadMarketCache(baseUrl)` later for the market summary. Move that call up so the recipe loop can use it, and track the first recipe for the verdict (Task 3). Replace the recipe loop (the `for (const [outputId, recipe] of snapshots.recipes)` block) with:

```ts
  // Load the market cache up front; recipe pricing and the verdict both need it.
  const cache = await loadMarketCache(baseUrl);

  let primaryRecipe: import('../lib/recipes').Recipe | null = null;
  let primaryMaterialCost = 0;

  for (const [outputId, recipe] of snapshots.recipes) {
    if (outputId !== itemId) continue;
    const priced = priceRecipe(recipe, cache.phantom, snapshots);
    if (!primaryRecipe) {
      primaryRecipe = recipe;
      primaryMaterialCost = priced.materialCost;
    }
    sources.push({
      type: 'recipe',
      jobId: 0,
      jobName: jobNameOf(recipe.classJob),
      level: recipe.recipeLevel,
      ingredients: priced.ingredients,
      materialCost: priced.materialCost,
      outputQty: recipe.amountResult ?? 1,
    });
  }
```

Remove the now-unused local `jobNames` map.

- [ ] **Step 3: Add item metadata to the response object**

Find the existing `return res.status(200).json({ itemId, itemName, sources, market });` near the end. Just before it, look up item meta:

```ts
  const meta = snapshots.itemsById.get(itemId);
```

Then change the response to include the meta fields (verdict added in Task 3):

```ts
  res.setHeader('Cache-Control', 'public, max-age=600');
  return res.status(200).json({
    itemId,
    itemName,
    ilvl: meta?.ilvl ?? 0,
    category: meta?.sc ? categoryLabel(meta.sc) : null,
    rarity: meta?.rarity ?? 0,
    canHq: meta?.canHq ?? false,
    sources,
    market,
  });
```

- [ ] **Step 4: Remove the now-duplicate market-cache load**

The market-summary block later in the handler calls `const cache = await loadMarketCache(baseUrl);` again inside its `try`. Delete that inner re-declaration and reuse the `cache` from Step 2 (the inner block can reference the outer `cache` directly).

- [ ] **Step 5: Typecheck**

Run: `cd /c/Users/esthe/Documents/Dev/ffxiv-helper && npx tsc --noEmit`
Expected: no errors in `plugin-item-sources.ts`. (If the repo's tsc surfaces unrelated pre-existing errors, confirm none are in the files you touched.)

- [ ] **Step 6: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/ffxiv-helper
git add src/api/plugin-item-sources.ts
git commit -m "feat(api): item-sources returns priced ingredients + item metadata"
```

---

## Task 3: Add the server-computed verdict to the endpoint

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\ffxiv-helper\src\api\plugin-item-sources.ts`

- [ ] **Step 1: Add imports**

```ts
import { computeVerdict } from '../features/items/verdict/computeVerdict';
```

And near the top of the file (module scope), add the home-world constant (matches `src/api/refresh-cache.ts`, which builds the cache):

```ts
const HOME_WORLD = process.env.HOME_WORLD ?? 'Phantom';
```

- [ ] **Step 2: Compute and flatten the verdict**

After the item `meta` lookup (Task 2, Step 3) and before the `return`, add:

```ts
  // Verdict — reuse the web app's tested computeVerdict. computeVerdict itself
  // returns an "untraded" result when there is no usable home price, so we only
  // skip it entirely when there is neither market data nor a recipe to assess.
  const phantomItem = cache.phantom?.[String(itemId)];
  let verdict: Record<string, unknown> | null = null;
  let runnerUp: Record<string, unknown> | null = null;
  if (phantomItem || primaryRecipe) {
    const vr = computeVerdict({
      phantom: phantomItem,
      region: cache.region?.[String(itemId)],
      recipe: primaryRecipe ?? undefined,
      vendorPrice: snapshots.vendorMap.get(itemId),
      materialCost: primaryMaterialCost,
      homeWorld: HOME_WORLD,
      canHq: meta?.canHq ?? false,
      now: Date.now(),
    });
    verdict = {
      headline: vr.best.headline,
      rationale: vr.best.rationale,
      bestPlay: vr.best.bestPlay,
      bestPlayDetail: vr.best.bestPlayDetail,
      netPerUnit: Math.round(vr.best.netPerUnit),
      gilPerDay: Math.round(vr.best.gilPerDay),
      roi: vr.best.roi,
      risk: vr.best.risk,
      tone: vr.best.tone,
      quality: vr.best.quality,
      kind: vr.best.kind,
    };
    runnerUp = vr.runnerUp
      ? { bestPlay: vr.runnerUp.bestPlay, gilPerDay: Math.round(vr.runnerUp.gilPerDay), kind: vr.runnerUp.kind }
      : null;
  }
```

- [ ] **Step 3: Include verdict in the response**

Update the `return res.status(200).json({...})` to add `verdict` and `runnerUp`:

```ts
  return res.status(200).json({
    itemId,
    itemName,
    ilvl: meta?.ilvl ?? 0,
    category: meta?.sc ? categoryLabel(meta.sc) : null,
    rarity: meta?.rarity ?? 0,
    canHq: meta?.canHq ?? false,
    sources,
    market,
    verdict,
    runnerUp,
  });
```

- [ ] **Step 4: Typecheck**

Run: `cd /c/Users/esthe/Documents/Dev/ffxiv-helper && npx tsc --noEmit`
Expected: no errors in `plugin-item-sources.ts`.

- [ ] **Step 5: Run the full backend test suite (no regressions)**

Run: `cd /c/Users/esthe/Documents/Dev/ffxiv-helper && npx vitest run src/api src/features/items/verdict`
Expected: PASS (existing verdict + api tests still green; new helper tests green).

- [ ] **Step 6: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/ffxiv-helper
git add src/api/plugin-item-sources.ts
git commit -m "feat(api): item-sources returns a server-computed verdict"
```

---

## Task 4: Plugin DTOs (`ApiClient.cs`)

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\qiqirn-companion\Services\ApiClient.cs`

No C# unit-test project exists in this repo; verification for plugin tasks is `dotnet build` (compiles against Dalamud) plus the manual in-game checks in Task 8.

- [ ] **Step 1: Extend `IngredientItem`**

Replace the `IngredientItem` record (around line 94) with:

```csharp
public record IngredientItem(
    [property: JsonPropertyName("itemId")]    int     ItemId,
    [property: JsonPropertyName("itemName")]  string  ItemName,
    [property: JsonPropertyName("qty")]       int     Qty,
    [property: JsonPropertyName("unitPrice")] int?    UnitPrice,
    [property: JsonPropertyName("source")]    string? Source
);
```

- [ ] **Step 2: Extend `RecipeSource`**

Add a `materialCost` property to the `RecipeSource` record (around line 100). Replace it with:

```csharp
public record RecipeSource(
    string                               Type,
    [property: JsonPropertyName("jobId")]        int                  JobId,
    [property: JsonPropertyName("jobName")]      string               JobName,
    [property: JsonPropertyName("level")]        int                  Level,
    [property: JsonPropertyName("ingredients")]  List<IngredientItem> Ingredients,
    [property: JsonPropertyName("outputQty")]    int                  OutputQty,
    [property: JsonPropertyName("materialCost")] int                  MaterialCost
) : ItemSource(Type);
```

- [ ] **Step 3: Add `Verdict` / `VerdictRunnerUp` records**

Immediately before the `ItemSourcesResponse` record (around line 174), add:

```csharp
public record Verdict(
    [property: JsonPropertyName("headline")]       string  Headline,
    [property: JsonPropertyName("rationale")]      string  Rationale,
    [property: JsonPropertyName("bestPlay")]       string  BestPlay,
    [property: JsonPropertyName("bestPlayDetail")] string  BestPlayDetail,
    [property: JsonPropertyName("netPerUnit")]     long    NetPerUnit,
    [property: JsonPropertyName("gilPerDay")]      long    GilPerDay,
    [property: JsonPropertyName("roi")]            double? Roi,
    [property: JsonPropertyName("risk")]           string  Risk,
    [property: JsonPropertyName("tone")]           string  Tone,
    [property: JsonPropertyName("quality")]        string  Quality,
    [property: JsonPropertyName("kind")]           string  Kind
);

public record VerdictRunnerUp(
    [property: JsonPropertyName("bestPlay")]  string BestPlay,
    [property: JsonPropertyName("gilPerDay")] long   GilPerDay,
    [property: JsonPropertyName("kind")]      string Kind
);
```

- [ ] **Step 4: Extend `ItemSourcesResponse`**

Replace the `ItemSourcesResponse` record with:

```csharp
public record ItemSourcesResponse(
    [property: JsonPropertyName("itemId")]   int               ItemId,
    [property: JsonPropertyName("itemName")] string            ItemName,
    [property: JsonPropertyName("ilvl")]     int               Ilvl,
    [property: JsonPropertyName("category")] string?           Category,
    [property: JsonPropertyName("rarity")]   int               Rarity,
    [property: JsonPropertyName("canHq")]    bool              CanHq,
    [property: JsonPropertyName("sources")]  List<ItemSource>  Sources,
    [property: JsonPropertyName("market")]   MarketSummary?    Market,
    [property: JsonPropertyName("verdict")]  Verdict?          Verdict,
    [property: JsonPropertyName("runnerUp")] VerdictRunnerUp?  RunnerUp
);
```

- [ ] **Step 5: Build**

Run: `cd /c/Users/esthe/Documents/Dev/qiqirn-companion && dotnet build QiqirnCompanion.csproj -c Debug`
Expected: Build succeeded, 0 errors. (Requires the Dalamud dev libs at `%AppData%\XIVLauncher\addon\Hooks\dev\`, present on the dev machine.)

- [ ] **Step 6: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/qiqirn-companion
git add Services/ApiClient.cs
git commit -m "feat: extend item-sources DTOs with item meta, prices, and verdict"
```

---

## Task 5: Reusable external-link helpers (`ItemInteractions.cs`)

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\qiqirn-companion\Services\ItemInteractions.cs`

- [ ] **Step 1: Add public link helpers**

Add these static methods to `ItemInteractions` (e.g. just below `Initialize`):

```csharp
    public static void OpenQiqirn(uint itemId)     => Util.OpenLink($"https://qiqirn.tools/item/{itemId}");
    public static void OpenGarland(uint itemId)    => Util.OpenLink($"https://www.garlandtools.org/db/#item/{itemId}");
    public static void OpenUniversalis(uint itemId) => Util.OpenLink($"https://universalis.app/market/{itemId}");
    public static void OpenGamerEscape(string name) => Util.OpenLink($"https://ffxiv.gamerescape.com/wiki/{Uri.EscapeDataString(name.Replace(' ', '_'))}");
```

- [ ] **Step 2: Use them in the context menu**

In `DrawContextMenu`, replace the four inline `Util.OpenLink(...)` calls with the helpers:

```csharp
        if (ImGui.MenuItem("View on Qiqirn.tools"))
            OpenQiqirn(itemId);
        if (ImGui.MenuItem("View on GarlandTools"))
            OpenGarland(itemId);
        if (ImGui.MenuItem("View on Universalis"))
            OpenUniversalis(itemId);
        if (ImGui.MenuItem("View on Gamer Escape"))
            OpenGamerEscape(name);
```

- [ ] **Step 3: Build**

Run: `cd /c/Users/esthe/Documents/Dev/qiqirn-companion && dotnet build QiqirnCompanion.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/qiqirn-companion
git add Services/ItemInteractions.cs
git commit -m "refactor: extract reusable external-link helpers in ItemInteractions"
```

---

## Task 6: Header block (chips + links) in `ItemInfoWindow.cs`

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\qiqirn-companion\Windows\ItemInfoWindow.cs`

- [ ] **Step 1: Add rarity color/label helpers**

Add these static helpers to the `ItemInfoWindow` class (near the existing `FormatGil`):

```csharp
    // FFXIV rarity tiers → name color (2 green, 3 blue, 4 purple, 7 pink); null = common.
    private static Vector4? RarityColor(int rarity) => rarity switch
    {
        2 => new Vector4(0.40f, 0.85f, 0.55f, 1f),
        3 => new Vector4(0.40f, 0.70f, 1.00f, 1f),
        4 => new Vector4(0.75f, 0.55f, 1.00f, 1f),
        7 => new Vector4(1.00f, 0.50f, 0.80f, 1f),
        _ => null,
    };

    private static string? RarityLabel(int rarity) => rarity switch
    {
        2 => "Uncommon",
        3 => "Rare",
        4 => "Aetherial",
        7 => "Legendary",
        _ => null,
    };

    // A small bordered metadata chip rendered inline on the current line.
    private static void DrawChip(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Border, color);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.BeginDisabled();
        ImGui.SmallButton(text);
        ImGui.EndDisabled();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }
```

- [ ] **Step 2: Add a header renderer that uses the loaded response**

Add a method that draws chips + links from `_sources` (the loaded `ItemSourcesResponse`):

```csharp
    private void DrawHeaderMeta(ItemSourcesResponse s)
    {
        var dim  = new Vector4(0.7f, 0.7f, 0.7f, 1f);
        var gold = new Vector4(0.9f, 0.75f, 0.3f, 1f);

        bool any = false;
        if (s.Ilvl > 1)               { DrawChip($"Item Lvl {s.Ilvl}", gold); any = true; }
        if (!string.IsNullOrEmpty(s.Category)) { ImGui.SameLine(); DrawChip(s.Category!, dim); any = true; }
        var rl = RarityLabel(s.Rarity);
        if (rl != null)               { ImGui.SameLine(); DrawChip(rl, RarityColor(s.Rarity) ?? dim); any = true; }
        if (s.CanHq)                  { ImGui.SameLine(); DrawChip("HQ-Capable", gold); any = true; }

        // External links.
        uint id = (uint)s.ItemId;
        if (any) ImGui.SameLine();
        if (ImGui.SmallButton("Garland")) ItemInteractions.OpenGarland(id);
        ImGui.SameLine();
        if (ImGui.SmallButton("GE"))      ItemInteractions.OpenGamerEscape(_itemName);
        ImGui.SameLine();
        if (ImGui.SmallButton("UV"))      ItemInteractions.OpenUniversalis(id);
        ImGui.Separator();
    }
```

- [ ] **Step 3: Use rarity color on the name and call the header**

In `Draw()`, replace the opening two lines:

```csharp
        ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.4f, 1), _itemName);
        ImGui.Separator();
```

with:

```csharp
        var nameColor = (_sources != null ? RarityColor(_sources.Rarity) : null)
                        ?? new Vector4(0.9f, 0.85f, 0.4f, 1);
        ImGui.TextColored(nameColor, _itemName);
        ImGui.Separator();
        if (_sources != null) DrawHeaderMeta(_sources);
```

- [ ] **Step 4: Build**

Run: `cd /c/Users/esthe/Documents/Dev/qiqirn-companion && dotnet build QiqirnCompanion.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/qiqirn-companion
git add Windows/ItemInfoWindow.cs
git commit -m "feat: item-info header chips, rarity color, and external links"
```

---

## Task 7: Verdict card in `ItemInfoWindow.cs`

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\qiqirn-companion\Windows\ItemInfoWindow.cs`

- [ ] **Step 1: Add a tone→color helper and the verdict renderer**

Add to the `ItemInfoWindow` class:

```csharp
    private static Vector4 ToneColor(string tone) => tone switch
    {
        "gold" => new Vector4(0.90f, 0.75f, 0.30f, 1f),
        "good" => new Vector4(0.40f, 0.85f, 0.55f, 1f),
        "aether" => new Vector4(0.40f, 0.70f, 1.00f, 1f),
        "warn" => new Vector4(0.90f, 0.75f, 0.30f, 1f),
        "bad"  => new Vector4(0.90f, 0.30f, 0.30f, 1f),
        _      => new Vector4(0.60f, 0.60f, 0.60f, 1f), // mute
    };

    private void DrawVerdict(Verdict v, VerdictRunnerUp? runnerUp)
    {
        var dim = new Vector4(0.75f, 0.75f, 0.75f, 1f);

        ImGui.TextColored(ToneColor(v.Tone), $"✦ {v.Headline}");
        ImGui.TextWrapped(v.Rationale);
        ImGui.Spacing();

        ImGui.TextColored(dim, "Best play:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.9f, 0.75f, 0.3f, 1f), v.BestPlay);
        ImGui.TextWrapped(v.BestPlayDetail);

        if (v.NetPerUnit > 0)
        {
            var green = new Vector4(0.40f, 0.85f, 0.55f, 1f);
            var roiStr = v.Roi.HasValue ? $"  ·  {Math.Round(v.Roi.Value * 100)}% ROI" : "";
            ImGui.TextColored(green, $"+{FormatGil(v.NetPerUnit)}/unit  ·  ~+{FormatGil(v.GilPerDay)}/day{roiStr}");
        }

        ImGui.TextColored(dim, $"Risk: {v.Risk}");

        if (runnerUp != null)
            ImGui.TextColored(dim, $"also viable: {runnerUp.BestPlay} · +{FormatGil(runnerUp.GilPerDay)}/day");

        ImGui.Separator();
    }
```

- [ ] **Step 2: Render the verdict above the sources**

In `Draw()`, inside the `if (_sources != null)` rendering path, draw the verdict before the sources list. Replace:

```csharp
        if (_sources != null)
            DrawSourcesList(_sources);
```

with:

```csharp
        if (_sources != null)
        {
            if (_sources.Verdict != null)
                DrawVerdict(_sources.Verdict, _sources.RunnerUp);
            DrawSourcesList(_sources);
        }
```

- [ ] **Step 3: Build**

Run: `cd /c/Users/esthe/Documents/Dev/qiqirn-companion && dotnet build QiqirnCompanion.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/qiqirn-companion
git add Windows/ItemInfoWindow.cs
git commit -m "feat: render make-vs-buy verdict card in item-info window"
```

---

## Task 8: Priced recipe table in `ItemInfoWindow.cs`

**Files:**
- Modify: `C:\Users\esthe\Documents\Dev\qiqirn-companion\Windows\ItemInfoWindow.cs`

- [ ] **Step 1: Add a source-tag color helper**

Add to the `ItemInfoWindow` class:

```csharp
    private static Vector4 SourceTagColor(string? source) => source switch
    {
        "vendor" => new Vector4(0.40f, 0.85f, 0.55f, 1f),
        "gather" => new Vector4(0.40f, 0.70f, 1.00f, 1f),
        "craft"  => new Vector4(0.90f, 0.75f, 0.30f, 1f),
        _        => new Vector4(0.55f, 0.70f, 0.90f, 1f), // mb / null
    };

    private static string SourceTagLabel(string? source) => source switch
    {
        "vendor" => "Vendor",
        "gather" => "Gather",
        "craft"  => "Craft",
        _        => "MB",
    };
```

- [ ] **Step 2: Rewrite `DrawRecipeSource` as a priced table**

Replace the existing `DrawRecipeSource` method with:

```csharp
    private void DrawRecipeSource(RecipeSource recipe)
    {
        ImGui.TextColored(new Vector4(0.2f, 0.8f, 1, 1), $"📖 {recipe.JobName} (Lv. {recipe.Level})");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"· Yield {recipe.OutputQty}");

        if (ImGui.BeginTable($"##recipe{recipe.JobName}{recipe.Level}", 5,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Ingredient", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty",        ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Unit",       ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Subtotal",   ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Source",     ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();

            foreach (var ing in recipe.Ingredients)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Selectable($"{ing.ItemName}##ing{ing.ItemId}");
                ItemInteractions.HandleRow((uint)ing.ItemId, ing.ItemName);

                ImGui.TableNextColumn();
                ImGui.Text($"x{ing.Qty}");

                ImGui.TableNextColumn();
                ImGui.Text(ing.UnitPrice.HasValue ? FormatGil(ing.UnitPrice.Value) : "—");

                ImGui.TableNextColumn();
                ImGui.Text(ing.UnitPrice.HasValue ? FormatGil((long)ing.UnitPrice.Value * ing.Qty) : "—");

                ImGui.TableNextColumn();
                ImGui.TextColored(SourceTagColor(ing.Source), SourceTagLabel(ing.Source));
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Material total (home)");
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.9f, 0.75f, 0.3f, 1), FormatGil(recipe.MaterialCost));
            ImGui.TableNextColumn();

            ImGui.EndTable();
        }
    }
```

Note: `FormatGil` takes a `long`; `UnitPrice` and `MaterialCost` are `int`/`int?` and widen implicitly, with an explicit `(long)` cast on the subtotal multiply to avoid `int` overflow.

- [ ] **Step 3: Build**

Run: `cd /c/Users/esthe/Documents/Dev/qiqirn-companion && dotnet build QiqirnCompanion.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Manual verification in-game**

With the backend changes deployed (or running locally) and the freshly built plugin reloaded via `/xldev`:

1. Open the item view for a known HQ-capable crafted item (e.g. a crafted weapon/armor piece):
   - Header shows Item Lvl, category, rarity tier (colored), and HQ-Capable chips, plus Garland/GE/UV buttons that open the right pages.
   - Verdict card shows a headline + rationale + best play; margin line appears when profitable.
   - Recipe table lists each ingredient with a price, subtotal, source tag, and a material total row.
2. Open an untraded / non-craftable item:
   - Verdict shows the "Not enough data" headline (or is hidden if there is also no recipe).
   - No recipe table is shown; other sources (vendor/gather/etc.) still render.

- [ ] **Step 5: Commit**

```bash
cd /c/Users/esthe/Documents/Dev/qiqirn-companion
git add Windows/ItemInfoWindow.cs
git commit -m "feat: priced recipe table with source tags in item-info window"
```

---

## Done

After Task 8, both repos have committed feature branches:
- `ffxiv-helper`: `feat/plugin-item-view-data`
- `qiqirn-companion`: `feat/item-view-header-verdict-recipe`

Deploy the backend so the endpoint returns the new fields, then the plugin renders them. Follow up by opening PRs in each repo (use `superpowers:finishing-a-development-branch` if desired).
