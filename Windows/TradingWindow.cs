using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

public class TradingWindow : Window, IDisposable
{
    private readonly ApiClient _api;

    // ── Preset definitions ────────────────────────────────────────────────
    private static readonly (string Id, string Label, string Category)[] Presets =
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
    ];

    // ── State ─────────────────────────────────────────────────────────────
    private string  _selectedPreset = "";
    private string? _world          = null;
    private bool    _isRunning      = false;
    private string? _error          = null;
    private List<TradingQueryRow> _rows = [];
    private int     _totalRows      = 0;

    public TradingWindow(ApiClient api) : base("Trading##trading")
    {
        _api = api;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 300),
            MaximumSize = new Vector2(1400, 900),
        };
    }

    public void SetWorld(string? world) => _world = world;

    public void DrawContent()
    {
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

        foreach (var (id, label, category) in Presets)
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

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders     |
            ImGuiTableFlags.RowBg       |
            ImGuiTableFlags.ScrollY     |
            ImGuiTableFlags.SizingFixedFit;

        var tableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (!ImGui.BeginTable("##tradingRows", 6, flags, new Vector2(0, tableHeight))) return;

        ImGui.TableSetupColumn("Item",       ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("HQ",         ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableSetupColumn("Price",      ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Avg",        ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Deal %",     ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Gil/day",    ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        foreach (var row in _rows)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (ImGui.Selectable(row.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                ImGui.SetClipboardText(row.Name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Click to copy");

            ImGui.TableSetColumnIndex(1);
            if (row.Hq)
                ImGui.TextColored(new Vector4(1, 0.85f, 0.1f, 1), "HQ");

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(FormatGil(row.UnitPrice));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextDisabled(FormatGil(row.AveragePrice));

            ImGui.TableSetColumnIndex(4);
            var dealColor = row.DealPct >= 40
                ? new Vector4(0.3f, 1f, 0.3f, 1f)
                : row.DealPct >= 20
                    ? new Vector4(1f, 0.85f, 0.1f, 1f)
                    : new Vector4(1f, 1f, 1f, 1f);
            ImGui.TextColored(dealColor, $"{row.DealPct}%");

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(FormatGil((long)row.GilFlow));
        }

        ImGui.EndTable();
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
