using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

public class CleanupWindow : Window, IDisposable
{
    private readonly ApiClient _api;

    private static readonly Vector4 Gold    = new(1.00f, 0.82f, 0.30f, 1f);
    private static readonly Vector4 Jade    = new(0.30f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Aether  = new(0.45f, 0.70f, 1.00f, 1f);
    private static readonly Vector4 Orange  = new(1.00f, 0.60f, 0.25f, 1f);
    private static readonly Vector4 Crimson = new(0.95f, 0.40f, 0.42f, 1f);
    private static readonly Vector4 Dim     = new(0.55f, 0.55f, 0.60f, 1f);

    private bool             _loading;
    private string?          _error;
    private bool             _scanned;
    private bool             _includeSaddlebag;
    private CleanupResponse? _result;

    public CleanupWindow(ApiClient api) : base("Inventory Cleanup##cleanup")
    {
        _api = api;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 400),
            MaximumSize = new Vector2(2200, 1400),
        };
    }

    public override void Draw() => DrawContent();

    public void DrawContent()
    {
        if (_loading) ImGui.BeginDisabled();
        if (ImGui.Button("Scan Inventory")) Scan();
        if (_loading) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.Checkbox("Include Saddlebag", ref _includeSaddlebag);

        if (_loading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Classifying...");
        }

        if (_error != null)
            ImGui.TextColored(Crimson, _error);

        ImGui.Separator();

        if (_result == null)
        {
            if (!_loading && _error == null)
                ImGui.TextDisabled(_scanned
                    ? "Nothing to clean up — your bags look tidy."
                    : "Click 'Scan Inventory' to triage your bags.");
            return;
        }

        DrawSummary(_result.Summary);
        ImGui.Spacing();

        DrawCraft(_result.Craft);
        DrawSellMb(_result.SellMb);
        DrawVendor(_result.Vendor);
        DrawDiscard(_result.Discard);
    }

    private void DrawSummary(CleanupSummary s)
    {
        ImGui.TextColored(Gold, $"Craft {s.CraftCount}");
        ImGui.SameLine(0, 6); ImGui.TextDisabled("•"); ImGui.SameLine(0, 6);
        ImGui.TextColored(Jade, $"Sell {s.SellMbCount}");
        ImGui.SameLine(0, 4); ImGui.TextDisabled($"(~{FormatGil((long)s.MbTotal)})");
        ImGui.SameLine(0, 6); ImGui.TextDisabled("•"); ImGui.SameLine(0, 6);
        ImGui.Text($"Vendor {s.VendorCount}");
        ImGui.SameLine(0, 4); ImGui.TextDisabled($"(~{FormatGil((long)s.VendorTotal)})");
        ImGui.SameLine(0, 6); ImGui.TextDisabled("•"); ImGui.SameLine(0, 6);
        ImGui.TextColored(Dim, $"Discard {s.DiscardCount}");
    }

    // ── Buckets ────────────────────────────────────────────────────────────

    private void DrawCraft(List<CleanupRow> rows)
    {
        if (!Header("Craft these", Gold, rows.Count, ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (rows.Count == 0) { ImGui.TextDisabled("  (none)"); return; }

        if (!BeginTable("##cleanCraft", 4)) return;
        ImGui.TableSetupColumn("Item",    ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty",     ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Makes",   ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Net/ea",  ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 90);
        ImGui.TableHeadersRow();

        ApplySort(rows, col => col switch
        {
            0 => (a, b) => string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Entry.Qty.CompareTo(b.Entry.Qty),
            2 => (a, b) => string.Compare(a.BestCraft?.OutputName ?? "", b.BestCraft?.OutputName ?? "", StringComparison.OrdinalIgnoreCase),
            3 => (a, b) => (a.BestCraft?.NetProfit ?? 0).CompareTo(b.BestCraft?.NetProfit ?? 0),
            _ => (Comparison<CleanupRow>?)null,
        });

        foreach (var r in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); DrawItemCell(r.Entry);
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(r.Entry.Qty.ToString());
            ImGui.TableSetColumnIndex(2);
            var c = r.BestCraft;
            if (c != null)
            {
                ImGui.TextUnformatted(c.OutputName);
                if (ImGui.IsItemHovered()) DrawCraftTooltip(c);
            }
            else ImGui.TextDisabled("—");
            ImGui.TableSetColumnIndex(3);
            var net = c?.NetProfit ?? 0;
            ImGui.TextColored(net > 0 ? Jade : Crimson, FormatGil((long)net));
        }
        ImGui.EndTable();
    }

    private void DrawSellMb(List<CleanupRow> rows)
    {
        if (!Header("Sell on Market", Jade, rows.Count, ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (rows.Count == 0) { ImGui.TextDisabled("  (none)"); return; }

        if (!BeginTable("##cleanSell", 6)) return;
        ImGui.TableSetupColumn("Item",     ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty",      ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Unit",     ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("Total",    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 80);
        ImGui.TableSetupColumn("Where",    ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Listings", ImGuiTableColumnFlags.WidthFixed, 65);
        ImGui.TableHeadersRow();

        ApplySort(rows, col => col switch
        {
            0 => (a, b) => string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Entry.Qty.CompareTo(b.Entry.Qty),
            2 => (a, b) => UnitOf(a).CompareTo(UnitOf(b)),
            3 => (a, b) => a.MbRevenue.CompareTo(b.MbRevenue),
            5 => (a, b) => a.MbListingCount.CompareTo(b.MbListingCount),
            _ => (Comparison<CleanupRow>?)null,
        });

        foreach (var r in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); DrawItemCell(r.Entry);
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(r.Entry.Qty.ToString());
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(FormatGil((long)UnitOf(r)));
            ImGui.TableSetColumnIndex(3); ImGui.TextColored(Jade, FormatGil((long)r.MbRevenue));
            ImGui.TableSetColumnIndex(4); DrawScope(r.MbScope);
            ImGui.TableSetColumnIndex(5);
            if (r.MbListingCount < 2) ImGui.TextColored(Orange, $"{r.MbListingCount} thin");
            else ImGui.TextDisabled(r.MbListingCount.ToString());
        }
        ImGui.EndTable();
    }

    private void DrawVendor(List<CleanupRow> rows)
    {
        if (!Header("Vendor", new Vector4(0.85f, 0.85f, 0.9f, 1f), rows.Count, ImGuiTreeNodeFlags.None)) return;
        if (rows.Count == 0) { ImGui.TextDisabled("  (none)"); return; }

        if (!BeginTable("##cleanVendor", 4)) return;
        ImGui.TableSetupColumn("Item",  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty",   ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Unit",  ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 80);
        ImGui.TableHeadersRow();

        ApplySort(rows, col => col switch
        {
            0 => (a, b) => string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Entry.Qty.CompareTo(b.Entry.Qty),
            2 => (a, b) => VendorUnitOf(a).CompareTo(VendorUnitOf(b)),
            3 => (a, b) => a.VendorRevenue.CompareTo(b.VendorRevenue),
            _ => (Comparison<CleanupRow>?)null,
        });

        foreach (var r in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); DrawItemCell(r.Entry);
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(r.Entry.Qty.ToString());
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(FormatGil((long)VendorUnitOf(r)));
            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(FormatGil((long)r.VendorRevenue));
        }
        ImGui.EndTable();
    }

    private void DrawDiscard(List<CleanupRow> rows)
    {
        if (!Header("Discard", Dim, rows.Count, ImGuiTreeNodeFlags.None)) return;
        if (rows.Count == 0) { ImGui.TextDisabled("  (none)"); return; }

        if (!BeginTable("##cleanDiscard", 2)) return;
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty",  ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();

        foreach (var r in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); DrawItemCell(r.Entry);
            ImGui.TableSetColumnIndex(1); ImGui.TextDisabled(r.Entry.Qty.ToString());
        }
        ImGui.EndTable();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static double UnitOf(CleanupRow r)       => r.Entry.Qty > 0 ? r.MbRevenue / r.Entry.Qty : 0;
    private static double VendorUnitOf(CleanupRow r)  => r.Entry.Qty > 0 ? r.VendorRevenue / r.Entry.Qty : 0;

    private static bool Header(string label, Vector4 color, int count, ImGuiTreeNodeFlags flags)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var open = ImGui.CollapsingHeader($"{label}  ({count})##{label}", flags);
        ImGui.PopStyleColor();
        return open;
    }

    private static bool BeginTable(string id, int cols)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.Sortable | ImGuiTableFlags.SizingFixedFit;
        return ImGui.BeginTable(id, cols, flags);
    }

    private static void DrawItemCell(CleanupEntry e)
    {
        ImGui.Selectable((e.IsHq ? "◆ " : "") + e.Name, false, ImGuiSelectableFlags.SpanAllColumns);
        ItemInteractions.HandleRow((uint)e.ItemId, e.Name, e.IsHq);
    }

    private static void DrawScope(string? scope)
    {
        switch (scope)
        {
            case "home":   ImGui.TextColored(Jade,   "Home");   break;
            case "dc":     ImGui.TextColored(Aether, "DC");     break;
            case "region": ImGui.TextColored(Orange, "Region"); break;
            default:       ImGui.TextDisabled("—");             break;
        }
    }

    private static void DrawCraftTooltip(CraftOpportunity c)
    {
        ImGui.BeginTooltip();
        ImGui.TextColored(Gold, $"{c.OutputName}  ({FormatGil((long)c.OutputUnitPrice)} ea)");
        if (c.UsedFromInventory is { Count: > 0 })
        {
            ImGui.TextDisabled("Uses from your inventory:");
            foreach (var u in c.UsedFromInventory) ImGui.BulletText($"{u.Name} x{u.Amount}");
        }
        if (c.MissingIngredients is { Count: > 0 })
        {
            ImGui.TextColored(Orange, "Still need to buy:");
            foreach (var m in c.MissingIngredients)
                ImGui.BulletText($"{m.Name} x{m.Amount}  (~{FormatGil((long)m.MbUnitPrice)} ea)");
        }
        ImGui.EndTooltip();
    }

    private static void ApplySort(List<CleanupRow> rows, Func<int, Comparison<CleanupRow>?> pick)
    {
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.SpecsDirty || specs.SpecsCount == 0) return;
        var spec = specs.Specs;
        var cmp = pick(spec.ColumnIndex);
        if (cmp != null)
        {
            var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
            rows.Sort((a, b) => asc ? cmp(a, b) : -cmp(a, b));
        }
        specs.SpecsDirty = false;
    }

    private static string FormatGil(long v) =>
        v >= 1_000_000 ? $"{v / 1_000_000.0:F1}M"
        : v >= 1_000   ? $"{v / 1_000.0:F0}k"
        : v.ToString();

    // ── Async ────────────────────────────────────────────────────────────────

    private void Scan()
    {
        _loading = true;
        _error   = null;
        _result  = null;

        List<(int id, int qty, bool hq)> inv;
        try
        {
            inv = InventoryReader.AggregatedForCleanup(_includeSaddlebag);
        }
        catch (Exception ex)
        {
            _error = $"Could not read inventory: {ex.Message}";
            _loading = false;
            return;
        }

        if (inv.Count == 0)
        {
            _error = "Inventory appears empty. Make sure you are logged in.";
            _loading = false;
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                _result = await _api.GetCleanupAsync(inv);
                _error  = null;
            }
            catch (Exception ex)
            {
                _error = $"Cleanup failed: {ex.Message}";
            }
            finally
            {
                _loading = false;
                _scanned = true;
            }
        });
    }

    public void Dispose() { }
}
