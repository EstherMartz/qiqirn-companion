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
        var nameColor = (_sources != null ? RarityColor(_sources.Rarity) : null)
                        ?? new Vector4(0.9f, 0.85f, 0.4f, 1);
        ImGui.TextColored(nameColor, _itemName);
        ImGui.Separator();
        if (_sources != null) DrawHeaderMeta(_sources);

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
        {
            if (_sources.Verdict != null)
                DrawVerdict(_sources.Verdict, _sources.RunnerUp);
            DrawSourcesList(_sources);
        }
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

    private void DrawHeaderMeta(ItemSourcesResponse s)
    {
        var dim  = new Vector4(0.7f, 0.7f, 0.7f, 1f);
        var gold = new Vector4(0.9f, 0.75f, 0.3f, 1f);

        bool any = false;
        if (s.Ilvl > 1)               { DrawChip($"Item Lvl {s.Ilvl}", gold); any = true; }
        if (!string.IsNullOrEmpty(s.Category)) { if (any) ImGui.SameLine(); DrawChip(s.Category!, dim); any = true; }
        var rl = RarityLabel(s.Rarity);
        if (rl != null)               { if (any) ImGui.SameLine(); DrawChip(rl, RarityColor(s.Rarity) ?? dim); any = true; }
        if (s.CanHq)                  { if (any) ImGui.SameLine(); DrawChip("HQ-Capable", gold); any = true; }

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
            if (source is null) continue;
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
