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
        ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.4f, 1), _itemName);
        ImGui.Separator();

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
            DrawSourcesList(_sources);
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
