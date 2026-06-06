using Dalamud.Bindings.ImGui;
using QiqirnCompanion.Models;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

/// <summary>
/// The Crafting Lists panel: import a qq:list: code, then view the active list's
/// resolved breakdown across LISTS / RECIPES / INGREDIENTS sub-tabs. Drawn as a
/// tab inside MainWindow via DrawContent().
/// </summary>
public class CraftListsWindow
{
    private readonly Configuration _config;
    private readonly ApiClient     _api;

    // Import box state
    private string         _codeInput   = string.Empty;
    private ImportedList?  _decoded     = null;  // preview of a valid pasted code
    private string         _listFilter  = string.Empty;

    // Active breakdown state
    private string         _activeId    = string.Empty;
    private ListBreakdown? _breakdown   = null;
    private bool           _loading     = false;
    private string         _error       = string.Empty;

    // Ingredients tab state
    private bool           _includeSaddlebag = false;
    private bool           _onlyHq           = false;
    private string         _exportStatus     = string.Empty;

    public CraftListsWindow(Configuration config, ApiClient api)
    {
        _config = config;
        _api    = api;
    }

    public void DrawContent()
    {
        if (!ImGui.BeginTabBar("##cl_subtabs")) return;
        DrawListsTab();
        DrawRecipesTab();
        DrawIngredientsTab();
        ImGui.EndTabBar();
    }

    private ImportedList? Active =>
        _config.ImportedLists.FirstOrDefault(l => l.Id == _config.ActiveListId);

    // ── LISTS tab ───────────────────────────────────────────────────────────
    private void DrawListsTab()
    {
        if (!ImGui.BeginTabItem("Lists")) return;

        ImGui.TextDisabled("Import from Qiqirn — paste a list code (the web 'Send to plugin' button)");
        ImGui.SetNextItemWidth(420);
        if (ImGui.InputTextWithHint("##clcode", "qq:list:v1:…", ref _codeInput, 8192))
            _decoded = ListCodec.Decode(_codeInput);

        if (_decoded != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f),
                $"✓ {_decoded.Name} — {_decoded.Items.Count} items · ready to import");
            ImGui.SameLine();
            if (ImGui.Button("Import##cl"))
            {
                _config.ImportedLists.Insert(0, _decoded);
                _config.ActiveListId = _decoded.Id;
                _config.Save();
                _codeInput = string.Empty;
                _decoded   = null;
                LoadBreakdown();
            }
        }
        else if (!string.IsNullOrWhiteSpace(_codeInput))
        {
            ImGui.TextDisabled("Not a valid qq:list:v1: code.");
        }

        ImGui.Separator();

        if (_config.ImportedLists.Count == 0)
        {
            ImGui.TextDisabled("No lists yet. Build one on qiqirn.tools, hit 'Send to plugin', and paste the code above.");
            ImGui.EndTabItem();
            return;
        }

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##clfilter", "Filter lists…", ref _listFilter, 100);

        ImGui.SameLine();
        if (ImGui.Button("Refresh##cl") && Active != null) LoadBreakdown();
        if (_loading) { ImGui.SameLine(); ImGui.TextDisabled("Loading…"); }
        if (!string.IsNullOrEmpty(_error)) ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _error);

        var q = _listFilter.Trim();
        foreach (var list in _config.ImportedLists.ToList())
        {
            if (q.Length > 0 && !list.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;

            var isActive = list.Id == _config.ActiveListId;
            if (ImGui.Selectable($"{list.Name}##cl{list.Id}", isActive))
            {
                _config.ActiveListId = list.Id;
                _config.Save();
                LoadBreakdown();
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"  {list.Items.Count} items");
            ImGui.SameLine();
            if (ImGui.SmallButton($"×##del{list.Id}"))
            {
                _config.ImportedLists.RemoveAll(l => l.Id == list.Id);
                if (_config.ActiveListId == list.Id)
                {
                    _config.ActiveListId = string.Empty;
                    _breakdown = null;
                }
                _config.Save();
            }
        }

        ImGui.EndTabItem();
    }

    // Placeholders filled in by Tasks 6 & 7.
    private void DrawRecipesTab()
    {
        if (!ImGui.BeginTabItem("Recipes")) return;
        ImGui.TextDisabled("Recipes view — coming in a later task.");
        ImGui.EndTabItem();
    }

    private void DrawIngredientsTab()
    {
        if (!ImGui.BeginTabItem("Ingredients")) return;

        if (_breakdown == null)
        {
            ImGui.TextDisabled(_loading ? "Loading…" : "Select a list in the Lists tab.");
            ImGui.EndTabItem();
            return;
        }

        ImGui.Checkbox("Include Saddlebag", ref _includeSaddlebag);
        ImGui.SameLine();
        ImGui.Checkbox("Only show HQ", ref _onlyHq);
        ImGui.SameLine();
        if (ImGui.Button("Export remaining as text")) ExportRemaining();
        if (!string.IsNullOrEmpty(_exportStatus)) { ImGui.SameLine(); ImGui.TextDisabled(_exportStatus); }

        // Live bag inventory (read on the framework draw thread — safe).
        Dictionary<int, int> inv;
        try { inv = InventoryReader.AggregatedBags(_includeSaddlebag); }
        catch { inv = new Dictionary<int, int>(); }

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        var height = ImGui.GetContentRegionAvail().Y;
        if (!ImGui.BeginTable("##cl_ingredients", 6, flags, new Vector2(0, height)))
        {
            ImGui.EndTabItem();
            return;
        }

        ImGui.TableSetupColumn("Item",         ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Required",     ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("In Inventory", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Remaining",    ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Source",       ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Used to Craft",ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var ing in _breakdown.Ingredients)
        {
            if (_onlyHq && ing.CanHq != true) continue;

            var have      = inv.GetValueOrDefault(ing.ItemId, 0);
            var remaining = Math.Max(0, ing.RequiredQty - have);
            var color     = RowColor(ing.Source, ing.RequiredQty, have);

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(color, "●");
            ImGui.SameLine();
            ImGui.Selectable(ing.ItemName);
            ItemInteractions.HandleRow((uint)ing.ItemId, ing.ItemName);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(ing.RequiredQty.ToString());

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(have.ToString());

            ImGui.TableSetColumnIndex(3);
            ImGui.TextColored(color, remaining.ToString());

            ImGui.TableSetColumnIndex(4);
            ImGui.TextUnformatted(SourceLabel(ing.Source));

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(ing.UsedToCraft.Count > 0 ? string.Join(", ", ing.UsedToCraft) : "—");
        }

        ImGui.EndTable();

        // Legend
        ImGui.TextDisabled("Green = have enough · Blue = will be crafted · Yellow = partial · Red = gather/buy.  (Retainers not counted.)");

        ImGui.EndTabItem();
    }

    private void ExportRemaining()
    {
        if (_breakdown == null) { _exportStatus = "Nothing to export"; return; }
        Dictionary<int, int> inv;
        try { inv = InventoryReader.AggregatedBags(_includeSaddlebag); }
        catch { inv = new Dictionary<int, int>(); }

        var lines = new List<string>();
        foreach (var ing in _breakdown.Ingredients)
        {
            if (_onlyHq && ing.CanHq != true) continue;
            var remaining = Math.Max(0, ing.RequiredQty - inv.GetValueOrDefault(ing.ItemId, 0));
            if (remaining > 0) lines.Add($"{ing.ItemName} x{remaining}");
        }
        if (lines.Count == 0) { _exportStatus = "Nothing remaining 🎉"; return; }
        ImGui.SetClipboardText(string.Join("\n", lines));
        _exportStatus = $"Copied {lines.Count} items";
    }

    // ── Helper methods ───────────────────────────────────────────────────────

    private static string SourceLabel(string source) => source switch
    {
        "Crafted"     => "CRAFTED",
        "Gathered"    => "GATHERED",
        "TimedGather" => "TIMED GATHER",
        "Vendor"      => "VENDOR",
        "Tome"        => "TOME / TOKEN",
        "Crystal"     => "CRYSTAL",
        _             => "MONSTER / OTHER",
    };

    private static Vector4 RowColor(string source, int required, int inInventory)
    {
        if (inInventory >= required) return new Vector4(0.4f, 0.9f, 0.4f, 1f);        // green: have enough
        if (source == "Crafted")     return new Vector4(0.45f, 0.7f, 1f, 1f);          // blue: will be crafted
        if (inInventory > 0)         return new Vector4(0.95f, 0.85f, 0.4f, 1f);       // yellow: partial
        return new Vector4(1f, 0.45f, 0.4f, 1f);                                       // red: need gather/buy
    }

    // ── Async ────────────────────────────────────────────────────────────────
    private void LoadBreakdown()
    {
        var list = Active;
        if (list == null) return;
        _activeId  = list.Id;
        _loading   = true;
        _error     = string.Empty;
        var items  = list.Items.ToList();

        Task.Run(async () =>
        {
            try
            {
                var bd = await _api.GetListBreakdownAsync(items);
                // Ignore a stale response if the user switched lists mid-flight.
                if (_activeId == list.Id) _breakdown = bd;
            }
            catch (Exception ex)
            {
                _error = $"Breakdown failed: {ex.Message}";
            }
            finally
            {
                _loading = false;
            }
        });
    }
}
