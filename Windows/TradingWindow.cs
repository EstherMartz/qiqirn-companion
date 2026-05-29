using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

public class TradingWindow : Window, IDisposable
{
    private readonly ApiClient _api;

    // ── Preset definitions ────────────────────────────────────────────────
    // Fallback only — the live list is fetched from the backend so a new
    // backend preset shows up without a plugin release. Kept in sync as a
    // sensible offline default.
    private static readonly (string Id, string Label, string Category)[] FallbackPresets =
    [
        ("mega-value-hq",        "Mega Value HQ",        "trading"),
        ("fast-sellers-hq",      "Fast Sellers HQ",      "trading"),
        ("food-potions",         "Food & Potions",        "trading"),
        ("furnishings",          "Furnishings",           "trading"),
        ("out-of-stock",         "Out of Stock",          "trading"),
        ("out-of-stock-nq",      "Out of Stock NQ",       "trading"),
        ("high-value-materials", "High-value Materials",  "trading"),
        ("minions-quick-sell",   "Minions Quick Sell",    "trading"),
        ("treasure-maps",        "Treasure Maps",         "trading"),
        ("glamour-gear",         "Glamour Gear",          "trading"),
        ("top-food",             "Top Food",              "trading"),
        ("top-fish",             "Top Fish",              "trading"),
        ("top-tinctures",        "Top Tinctures",         "trading"),
        ("top-dyes",             "Top Dyes",              "trading"),
        ("top-materia",          "Top Materia",           "trading"),
        ("top-minions",          "Top Minions",           "trading"),
        ("gather-commodities",   "Gatherer Commodities",  "gathering"),
        ("mining-commodities",   "Mining",                "gathering"),
        ("botany-commodities",   "Botany",                "gathering"),
        ("fishing-commodities",  "Fishing",               "gathering"),
        ("intermediate-materials","Intermediate Materials","crafting"),
        ("craftable-housing",    "Craftable Housing",     "crafting"),
    ];

    // ── State ─────────────────────────────────────────────────────────────
    private string  _selectedPreset = "";
    private string? _world          = null;
    private bool    _isRunning      = false;
    private string? _error          = null;
    private List<TradingQueryRow> _rows = [];
    private int     _totalRows      = 0;
    private string? _lastMode       = null;   // "standard" | "craft"

    // Live preset list (backend-driven; starts from the fallback).
    private List<(string Id, string Label, string Category)> _presets;
    private bool _presetsRequested;

    public TradingWindow(ApiClient api) : base("Trading##trading")
    {
        _api = api;
        _presets = new List<(string, string, string)>(FallbackPresets);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 300),
            MaximumSize = new Vector2(1400, 900),
        };
    }

    public void SetWorld(string? world) => _world = world;

    // Fetch the preset catalog from the backend once; falls back to the built-in
    // list on failure. Keeps the plugin's buttons in sync with the server.
    private void EnsurePresetsLoaded()
    {
        if (_presetsRequested) return;
        _presetsRequested = true;
        Task.Run(async () =>
        {
            try
            {
                var list = await _api.GetTradingPresetsAsync();
                if (list is { Count: > 0 })
                    _presets = list.Select(p => (p.Id, p.Label, p.Category)).ToList();
            }
            catch
            {
                // keep the fallback list
            }
        });
    }

    public void DrawContent()
    {
        EnsurePresetsLoaded();
        DrawPresetBar();
        ImGui.Separator();
        DrawResults();
    }

    public override void Draw() => DrawContent();

    // ── Preset bar ────────────────────────────────────────────────────────

    private void DrawPresetBar()
    {
        string? lastCategory = null;
        bool firstInRow = true;

        foreach (var (id, label, category) in _presets)
        {
            if (lastCategory != null && lastCategory != category)
            {
                ImGui.Spacing();
                firstInRow = true;
            }
            lastCategory = category;

            if (!firstInRow) ImGui.SameLine();
            firstInRow = false;

            bool isSelected = _selectedPreset == id;
            if (isSelected)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 1f, 1f));

            if (ImGui.Button(label))
            {
                _selectedPreset = id;
                RunPreset(id);
            }

            if (isSelected)
                ImGui.PopStyleColor();
        }

        if (_isRunning)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Running query...");
        }

        if (_error != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _error);
        }
    }

    // ── Results table ─────────────────────────────────────────────────────

    private void DrawResults()
    {
        if (_rows.Count == 0 && !_isRunning)
        {
            if (_error == null)
                ImGui.TextDisabled("Pick a preset above to run a query.");
            return;
        }

        if (_rows.Count > 0)
            ImGui.TextDisabled($"{_rows.Count} results");

        var tableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (_lastMode == "craft") DrawCraftTable(tableHeight);
        else DrawStandardTable(tableHeight);
    }

    private const ImGuiTableFlags TableFlags =
        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
        | ImGuiTableFlags.Sortable | ImGuiTableFlags.SizingFixedFit;

    private static readonly Vector4 JadeC    = new(0.30f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 CrimsonC = new(0.95f, 0.40f, 0.42f, 1f);

    private void DrawStandardTable(float tableHeight)
    {
        if (!ImGui.BeginTable("##tradingRows", 8, TableFlags, new Vector2(0, tableHeight))) return;

        ImGui.TableSetupColumn("Item",     ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("HQ",       ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableSetupColumn("Price",    ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Avg",      ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Deal %",   ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Sales/day",ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Gil/day",  ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 80);
        ImGui.TableSetupColumn("Cheapest", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableHeadersRow();

        SortStandard();

        foreach (var row in _rows)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Selectable(row.Name, false, ImGuiSelectableFlags.SpanAllColumns);
            ItemInteractions.HandleRow((uint)row.Id, row.Name, row.Hq);

            ImGui.TableSetColumnIndex(1);
            if (row.Hq) ImGui.TextColored(new Vector4(1, 0.85f, 0.1f, 1), "HQ");

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(FormatGil((long)row.UnitPrice));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextDisabled(FormatGil((long)row.AveragePrice));

            ImGui.TableSetColumnIndex(4);
            var dealColor = row.DealPct >= 40
                ? new Vector4(0.3f, 1f, 0.3f, 1f)
                : row.DealPct >= 20
                    ? new Vector4(1f, 0.85f, 0.1f, 1f)
                    : new Vector4(1f, 1f, 1f, 1f);
            ImGui.TextColored(dealColor, $"{row.DealPct}%");

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(row.Velocity > 0 ? row.Velocity.ToString("F1") : "—");

            ImGui.TableSetColumnIndex(6);
            ImGui.TextUnformatted(FormatGil((long)row.GilFlow));

            ImGui.TableSetColumnIndex(7);
            if (row.CheapestWorld != null && row.CheapestPrice.HasValue)
                ImGui.TextUnformatted($"{row.CheapestWorld} @ {FormatGil((long)row.CheapestPrice.Value)}");
            else
                ImGui.TextDisabled("—");
        }

        ImGui.EndTable();
    }

    private void DrawCraftTable(float tableHeight)
    {
        if (!ImGui.BeginTable("##craftRows", 7, TableFlags, new Vector2(0, tableHeight))) return;

        ImGui.TableSetupColumn("Item",      ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("HQ",        ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableSetupColumn("Sale",      ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Mat cost",  ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Profit",    ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Sales/day", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Gil/day",   ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 90);
        ImGui.TableHeadersRow();

        SortCraft();

        foreach (var row in _rows)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Selectable(row.Name, false, ImGuiSelectableFlags.SpanAllColumns);
            ItemInteractions.HandleRow((uint)row.Id, row.Name, row.Hq);

            ImGui.TableSetColumnIndex(1);
            if (row.Hq) ImGui.TextColored(new Vector4(1, 0.85f, 0.1f, 1), "HQ");

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(FormatGil((long)row.UnitPrice));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextDisabled(FormatGil((long)(row.MaterialCost ?? 0)));

            ImGui.TableSetColumnIndex(4);
            var profit = row.Profit ?? 0;
            ImGui.TextColored(profit > 0 ? JadeC : CrimsonC, FormatGil((long)profit));

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(row.Velocity > 0 ? row.Velocity.ToString("F1") : "—");

            ImGui.TableSetColumnIndex(6);
            ImGui.TextUnformatted(FormatGil((long)(row.GilPerDay ?? row.GilFlow)));
        }

        ImGui.EndTable();
    }

    private void SortStandard()
    {
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.SpecsDirty || specs.SpecsCount == 0) return;

        var spec = specs.Specs;
        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
        Comparison<TradingQueryRow> cmp = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Hq.CompareTo(b.Hq),
            2 => (a, b) => a.UnitPrice.CompareTo(b.UnitPrice),
            3 => (a, b) => a.AveragePrice.CompareTo(b.AveragePrice),
            4 => (a, b) => a.DealPct.CompareTo(b.DealPct),
            5 => (a, b) => a.Velocity.CompareTo(b.Velocity),
            6 => (a, b) => a.GilFlow.CompareTo(b.GilFlow),
            7 => (a, b) => (a.CheapestPrice ?? double.MaxValue).CompareTo(b.CheapestPrice ?? double.MaxValue),
            _ => (a, b) => 0,
        };
        _rows.Sort((a, b) => asc ? cmp(a, b) : -cmp(a, b));
        specs.SpecsDirty = false;
    }

    private void SortCraft()
    {
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.SpecsDirty || specs.SpecsCount == 0) return;

        var spec = specs.Specs;
        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
        Comparison<TradingQueryRow> cmp = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Hq.CompareTo(b.Hq),
            2 => (a, b) => a.UnitPrice.CompareTo(b.UnitPrice),
            3 => (a, b) => (a.MaterialCost ?? 0).CompareTo(b.MaterialCost ?? 0),
            4 => (a, b) => (a.Profit ?? 0).CompareTo(b.Profit ?? 0),
            5 => (a, b) => a.Velocity.CompareTo(b.Velocity),
            6 => (a, b) => (a.GilPerDay ?? a.GilFlow).CompareTo(b.GilPerDay ?? b.GilFlow),
            _ => (a, b) => 0,
        };
        _rows.Sort((a, b) => asc ? cmp(a, b) : -cmp(a, b));
        specs.SpecsDirty = false;
    }

    private static string FormatGil(long v) =>
        v >= 1_000_000 ? $"{v / 1_000_000.0:F1}M"
        : v >= 1_000   ? $"{v / 1_000.0:F0}k"
        : v.ToString();

    // ── Async helpers ─────────────────────────────────────────────────────

    private void RunPreset(string presetId)
    {
        _isRunning = true;
        _error     = null;
        _rows      = [];

        Task.Run(async () =>
        {
            try
            {
                var result = await _api.RunTradingQueryAsync(presetId, _world);
                _rows      = result?.Rows ?? [];
                _totalRows = result?.Total ?? 0;
                _lastMode  = result?.Mode ?? "standard";
                _error     = null;
            }
            catch (Exception ex)
            {
                _error = $"Query failed: {ex.Message}";
            }
            finally
            {
                _isRunning = false;
            }
        });
    }

    public void Dispose() { }
}
