using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly ApiClient     _api;
    private readonly IPlayerState  _playerState;
    private readonly SearchWindow  _searchWindow;
    private readonly TradingWindow _tradingWindow;
    private readonly PlannerWindow _plannerWindow;
    private readonly CleanupWindow _cleanupWindow;
    private readonly SettingsPanel  _settingsPanel;

    // ── Projects tab state ────────────────────────────────────────────────────
    private List<ApiProject>    _projects            = [];
    private int                 _selectedProjectIndex = 0;
    private ApiProjectDetail?   _projectDetail;
    private bool                _projectsLoading     = false;
    private string              _projectsError       = string.Empty;
    private bool                _claimInProgress     = false;
    private string              _claimError          = string.Empty;
    private string?             _selectedPhaseKey    = null;   // "partKey\0phaseIndex"; null = all
    private readonly Dictionary<int, int> _progressAmounts = new();  // per-task "Add" input value


    // New-project form state
    private bool                       _showNewProject     = false;
    private string                     _newProjectSearch   = string.Empty;
    private List<ItemSearchResult>     _newProjectResults  = [];
    private ItemSearchResult?          _newProjectSelected = null;
    private int                        _newProjectQty      = 1;
    private string                     _newProjectName     = string.Empty;
    private bool                       _newProjectBusy     = false;
    private string                     _newProjectError    = string.Empty;
    private string                     _newProjectList     = string.Empty;
    // ── Crafting tab state ────────────────────────────────────────────────────
    private List<CraftableItem> _craftable        = [];
    private bool                _craftLoading     = false;
    private string              _craftError       = string.Empty;
    private bool                _includeSaddlebag = false;
    private bool                _craftScanned     = false;
    private int                 _maxMissing       = 0;
    private string              _craftExportStatus = string.Empty;

    public MainWindow(Configuration config, ApiClient api, IPlayerState playerState, SearchWindow searchWindow, TradingWindow tradingWindow, PlannerWindow plannerWindow, CleanupWindow cleanupWindow, SettingsPanel settingsPanel)
        : base("Qiqirn Companion##main", ImGuiWindowFlags.None)
    {
        _config        = config;
        _api           = api;
        _playerState   = playerState;
        _searchWindow  = searchWindow;
        _tradingWindow = tradingWindow;
        _plannerWindow = plannerWindow;
        _cleanupWindow = cleanupWindow;
        _settingsPanel = settingsPanel;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(2200, 1400),
        };
    }

    private string CharacterName =>
        !string.IsNullOrEmpty(_config.CharacterNameOverride)
            ? _config.CharacterNameOverride
            : (!string.IsNullOrEmpty(_playerState.CharacterName)
                ? _playerState.CharacterName
                : "(not in game)");

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##tabs")) return;

        DrawTradingTab();
        DrawSearchTab();
        DrawPlannerTab();
        DrawProjectsTab();
        DrawCraftingTab();
        DrawCleanupTab();
        DrawSettingsTab();

        ImGui.EndTabBar();
    }

    private void DrawPlannerTab()
    {
        if (!ImGui.BeginTabItem("Planner")) return;
        _plannerWindow.DrawContent();
        ImGui.EndTabItem();
    }

    private void DrawCleanupTab()
    {
        if (!ImGui.BeginTabItem("Cleanup")) return;
        _cleanupWindow.DrawContent();
        ImGui.EndTabItem();
    }

    private void DrawSettingsTab()
    {
        if (!ImGui.BeginTabItem("Settings")) return;
        _settingsPanel.DrawContent();
        ImGui.EndTabItem();
    }

    private void DrawTradingTab()
    {
        if (!ImGui.BeginTabItem("Trading")) return;
        // Pass world to trading window so home-scope presets work
        _tradingWindow.SetWorld(!string.IsNullOrEmpty(_playerState.CharacterName) ? GetWorldName() : null);
        _tradingWindow.DrawContent();
        ImGui.EndTabItem();
    }

    private string? GetWorldName()
    {
        // Returns the configured home world (auto-filled from IPlayerState.HomeWorld
        // by the Settings panel). Empty until detected/set — DC-scope presets still work.
        return _config.HomeWorld;
    }

    // ── Search tab ────────────────────────────────────────────────────────────

    private void DrawSearchTab()
    {
        if (!ImGui.BeginTabItem("Search")) return;
        _searchWindow.DrawContent();
        ImGui.EndTabItem();
    }

    // ── Projects tab ──────────────────────────────────────────────────────────

    private void DrawProjectsTab()
    {
        if (!ImGui.BeginTabItem("Projects")) return;

        // Auto-load on first open; manual refresh via button.
        if (ImGui.Button("Refresh") || (_projects.Count == 0 && !_projectsLoading && string.IsNullOrEmpty(_projectsError)))
        {
            LoadProjects();
        }

        if (_projectsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Loading...");
        }

        if (!string.IsNullOrEmpty(_projectsError))
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _projectsError);
        }

        if (_projects.Count > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(280);
            var projectNames = _projects.ConvertAll(p => p.Name).ToArray();
            if (ImGui.Combo("##project", ref _selectedProjectIndex, projectNames, projectNames.Length))
            {
                LoadProjectDetail(_projects[_selectedProjectIndex].Id);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(_showNewProject ? "Cancel" : "＋ New Project"))
        {
            _showNewProject = !_showNewProject;
            _newProjectError = string.Empty;
        }

        if (_showNewProject)
            DrawNewProjectForm();

        ImGui.Separator();

        if (_projectDetail is not null)
        {
            DrawPhaseBar(_projectDetail);
            DrawTasksTable(_projectDetail);
        }

        // Footer
        ImGui.Spacing();
        ImGui.TextDisabled($"Claiming as: {CharacterName}");

        if (!string.IsNullOrEmpty(_claimError))
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _claimError);
        }

        ImGui.EndTabItem();
    }

    // A task's phase identity, or null for standard (non-phased) crafts.
    private static string? PhaseKeyOf(ApiTask t) =>
        !string.IsNullOrEmpty(t.Meta?.PartKey) ? $"{t.Meta!.PartKey}\0{t.Meta!.PhaseIndex ?? 0}" : null;

    private void DrawPhaseBar(ApiProjectDetail detail)
    {
        // Collect distinct phases with done/total counts (mirrors web collectPhases).
        var order = new List<string>();
        var totals = new Dictionary<string, int>();
        var done = new Dictionary<string, int>();
        foreach (var t in detail.Tasks)
        {
            var key = PhaseKeyOf(t);
            if (key == null) continue;
            if (!totals.ContainsKey(key)) { order.Add(key); totals[key] = 0; done[key] = 0; }
            totals[key]++;
            if (t.Status == "done") done[key]++;
        }

        if (order.Count < 2) { _selectedPhaseKey = null; return; }  // single/standard project

        if (ImGui.Button(_selectedPhaseKey == null ? "[All]" : "All"))
            _selectedPhaseKey = null;

        foreach (var key in order)
        {
            ImGui.SameLine();
            var parts = key.Split('\0');
            var label = $"{parts[0]} · P{int.Parse(parts[1]) + 1}  {done[key]}/{totals[key]}";
            var selected = _selectedPhaseKey == key;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 1f, 1f));
            if (ImGui.Button($"{label}##phase{key}"))
                _selectedPhaseKey = key;
            if (selected) ImGui.PopStyleColor();
        }
        ImGui.Spacing();
    }

    private void DrawTasksTable(ApiProjectDetail detail)
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders     |
            ImGuiTableFlags.RowBg       |
            ImGuiTableFlags.ScrollY     |
            ImGuiTableFlags.Sortable    |
            ImGuiTableFlags.SizingFixedFit;

        // Reserve height for the footer line below the table.
        var tableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2;
        if (!ImGui.BeginTable("##tasks", 5, flags, new Vector2(0, tableHeight))) return;

        ImGui.TableSetupColumn("Item",     ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty",      ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status",   ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Assignee", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("",         ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 200);
        ImGui.TableHeadersRow();

        SortTasksIfNeeded(detail.Tasks);

        foreach (var task in detail.Tasks)
        {
            if (_selectedPhaseKey != null && PhaseKeyOf(task) != _selectedPhaseKey) continue;

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Selectable(task.ItemName);
            ItemInteractions.HandleRow((uint)task.ItemId, task.ItemName);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted($"{task.QtyDone}/{task.QtyNeeded}");

            ImGui.TableSetColumnIndex(2);
            var statusColor = task.Status switch
            {
                "done"    => new Vector4(0.4f, 0.9f, 0.4f, 1),
                "claimed" => new Vector4(0.9f, 0.9f, 0.4f, 1),
                _         => new Vector4(1,    1,    1,    1),
            };
            ImGui.TextColored(statusColor, task.Status);

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(ResolveAssignee(detail, task));

            ImGui.TableSetColumnIndex(4);
            ImGui.PushID(task.Id);
            if (_claimInProgress) ImGui.BeginDisabled();
            if (task.Status == "open")
            {
                if (ImGui.SmallButton("Claim"))
                    ClaimTask(detail.Project.Id, task.Id);
            }
            else if (task.Status == "claimed" && IsMyClaim(task))
            {
                // Only the claimer can edit progress. The input is an item count in
                // [0, qtyNeeded]: "Add" adds it to your progress (capped server-side),
                // "Set" overwrites the total — the way to fix an over-log.
                var amt = _progressAmounts.TryGetValue(task.Id, out var v) ? v : 1;
                amt = Math.Clamp(amt, 0, task.QtyNeeded);

                ImGui.SetNextItemWidth(46);
                if (ImGui.InputInt("##amt", ref amt, 0, 0))
                    _progressAmounts[task.Id] = Math.Clamp(amt, 0, task.QtyNeeded);

                ImGui.SameLine();
                if (amt < 1) ImGui.BeginDisabled();
                if (ImGui.SmallButton("Add"))
                    LogProgress(detail.Project.Id, task.Id, amt);
                if (amt < 1) ImGui.EndDisabled();
                else if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add this many to your progress");

                ImGui.SameLine();
                if (ImGui.SmallButton("Set"))
                    SetProgress(detail.Project.Id, task.Id, amt);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Set total done to this value (0–{task.QtyNeeded}) — use to fix mistakes");

                ImGui.SameLine();
                if (ImGui.SmallButton("Done"))
                    CompleteTask(detail.Project.Id, task.Id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Mark fully complete");
            }
            if (_claimInProgress) ImGui.EndDisabled();
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    // Discord IDs resolve via userNames; character-claims store the name directly
    // as the assignee id, so the fallback already shows a readable name.
    private static string ResolveAssignee(ApiProjectDetail detail, ApiTask task)
    {
        if (task.AssigneeName is { Length: > 0 }) return task.AssigneeName;
        if (task.AssigneeId is { Length: > 0 } id)
        {
            if (detail.UserNames != null && detail.UserNames.TryGetValue(id, out var name) && !string.IsNullOrEmpty(name))
                return name;
            return id;
        }
        return "—";
    }

    // Maps a backend task source ("craft"/"gather"/…) to the Category column /
    // filter label. Mirrors ffxiv-helper's craftRender.ts source set.
    private static string CategoryLabel(string? source) => source switch
    {
        "craft"    => "Craft",
        "workshop" => "Workshop",
        "gather"   => "Gather",
        "market"   => "Market",
        "vendor"   => "Vendor",
        "currency" => "Currency",
        _          => "—",
    };

    // Maps a task's crafter job code (meta.job) to a full job name. Only craft/
    // workshop tasks carry one; gather/market/vendor/currency show an em dash.
    private static string JobLabel(TaskMeta? meta) => meta?.Job switch
    {
        "CRP" => "Carpenter",
        "BSM" => "Blacksmith",
        "ARM" => "Armorer",
        "GSM" => "Goldsmith",
        "LTW" => "Leatherworker",
        "WVR" => "Weaver",
        "ALC" => "Alchemist",
        "CUL" => "Culinarian",
        "ANY" => "Any",
        _     => "—",
    };

    private void SortTasksIfNeeded(List<ApiTask> tasks)
    {
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.SpecsDirty || specs.SpecsCount == 0) return;

        var spec = specs.Specs;
        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
        Comparison<ApiTask> cmp = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.QtyNeeded.CompareTo(b.QtyNeeded),
            2 => (a, b) => string.Compare(a.Status, b.Status, StringComparison.OrdinalIgnoreCase),
            3 => (a, b) => string.Compare(a.AssigneeId ?? "", b.AssigneeId ?? "", StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => 0,
        };
        tasks.Sort((a, b) => asc ? cmp(a, b) : -cmp(a, b));
        specs.SpecsDirty = false;
    }

    // ── Crafting tab ──────────────────────────────────────────────────────────

    private void DrawCraftingTab()
    {
        if (!ImGui.BeginTabItem("Crafting")) return;

        if (_craftLoading) ImGui.BeginDisabled();
        if (ImGui.Button("Scan Inventory"))
            ScanInventory();
        if (_craftLoading) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.Checkbox("Include Saddlebag", ref _includeSaddlebag);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("Max missing", ref _maxMissing))
            _maxMissing = Math.Clamp(_maxMissing, 0, 5);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("0 = only items you can fully craft.\nHigher also lists near-complete crafts missing up to N ingredient types.\nRe-scan to apply.");

        var canExport = _craftable.Exists(c => c.Qty > 0);
        ImGui.SameLine();
        if (!canExport) ImGui.BeginDisabled();
        if (ImGui.Button("Export to Text"))
            ExportCraftableToText();
        if (!canExport) ImGui.EndDisabled();
        if (!string.IsNullOrEmpty(_craftExportStatus))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_craftExportStatus);
        }

        if (_craftLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Scanning...");
        }

        if (!string.IsNullOrEmpty(_craftError))
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _craftError);
        }

        ImGui.Separator();

        if (_craftable.Count == 0 && !_craftLoading && string.IsNullOrEmpty(_craftError))
        {
            ImGui.TextDisabled(_craftScanned
                ? "No craftable items found. Try including Saddlebag or check your inventory."
                : "Click 'Scan Inventory' to see what you can craft.");
        }
        else
            DrawCraftableTable();

        ImGui.EndTabItem();
    }

    private void DrawCraftableTable()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders     |
            ImGuiTableFlags.RowBg       |
            ImGuiTableFlags.ScrollY     |
            ImGuiTableFlags.Sortable    |
            ImGuiTableFlags.SizingFixedFit;

        var tableHeight = ImGui.GetContentRegionAvail().Y;
        if (!ImGui.BeginTable("##craftable", 6, flags, new Vector2(0, tableHeight))) return;

        ImGui.TableSetupColumn("Item",      ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Can Make",  ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Missing",   ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Sales/day", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Cheapest",  ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Min NQ",    ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        SortCraftableIfNeeded();

        foreach (var item in _craftable)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.Selectable(item.Name, false, ImGuiSelectableFlags.SpanAllColumns);
            ItemInteractions.HandleRow((uint)item.ItemId, item.Name);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(item.Qty > 0 ? item.Qty.ToString() : "—");

            ImGui.TableSetColumnIndex(2);
            if (item.MissingCount > 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), item.MissingCount.ToString());
                if (ImGui.IsItemHovered())
                    DrawMissingTooltip(item);
            }
            else
            {
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "✓");
            }

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(item.Velocity > 0 ? item.Velocity.ToString("F1") : "—");

            ImGui.TableSetColumnIndex(4);
            if (item.CheapestWorld != null && item.CheapestPrice.HasValue)
                ImGui.TextUnformatted($"{item.CheapestWorld} @ {FormatGil(item.CheapestPrice.Value)}");
            else
                ImGui.TextDisabled("—");

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(item.MinNQ.HasValue ? FormatGil(item.MinNQ.Value) : "—");
        }

        ImGui.EndTable();
    }

    private static void DrawMissingTooltip(CraftableItem item)
    {
        if (item.Ingredients == null) return;
        ImGui.BeginTooltip();
        ImGui.TextDisabled("Missing ingredients:");
        foreach (var ing in item.Ingredients)
        {
            if (ing.Have >= ing.Needed) continue;
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.4f, 1f), $"{ing.Name}: {ing.Have}/{ing.Needed}");
        }
        ImGui.EndTooltip();
    }

    private static string FormatGil(long v) =>
        v >= 1_000_000 ? $"{v / 1_000_000.0:F1}M"
        : v >= 1_000   ? $"{v / 1_000.0:F0}k"
        : v.ToString();

    private void SortCraftableIfNeeded()
    {
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.SpecsDirty || specs.SpecsCount == 0) return;

        var spec = specs.Specs;
        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
        Comparison<CraftableItem> cmp = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Qty.CompareTo(b.Qty),
            2 => (a, b) => a.MissingCount.CompareTo(b.MissingCount),
            3 => (a, b) => a.Velocity.CompareTo(b.Velocity),
            4 => (a, b) => (a.CheapestPrice ?? int.MaxValue).CompareTo(b.CheapestPrice ?? int.MaxValue),
            5 => (a, b) => (a.MinNQ ?? int.MaxValue).CompareTo(b.MinNQ ?? int.MaxValue),
            _ => (a, b) => 0,
        };
        _craftable.Sort((a, b) => asc ? cmp(a, b) : -cmp(a, b));
        specs.SpecsDirty = false;
    }

    // ── Async helpers ─────────────────────────────────────────────────────────

    private void DrawNewProjectForm()
    {
        ImGui.Separator();
        ImGui.TextDisabled("Create a new crafting project");

        // Item search box
        ImGui.SetNextItemWidth(280);
        if (ImGui.InputTextWithHint("##npsearch", "Search item…", ref _newProjectSearch, 100))
            SearchNewProjectItems();
        ImGui.SameLine();
        if (ImGui.Button("Search##np"))
            SearchNewProjectItems();

        // Results combo
        if (_newProjectResults.Count > 0)
        {
            var names = _newProjectResults.ConvertAll(r => r.Name).ToArray();
            var idx = _newProjectSelected != null
                ? _newProjectResults.FindIndex(r => r.Id == _newProjectSelected.Id)
                : -1;
            ImGui.SetNextItemWidth(280);
            if (ImGui.Combo("##npresult", ref idx, names, names.Length) && idx >= 0)
                _newProjectSelected = _newProjectResults[idx];
        }

        if (_newProjectSelected != null)
            ImGui.TextDisabled($"Selected: {_newProjectSelected.Name}");

        // Qty + optional name
        ImGui.SetNextItemWidth(110);
        if (ImGui.InputInt("Qty##np", ref _newProjectQty))
            _newProjectQty = Math.Clamp(_newProjectQty, 1, 99999);

        ImGui.SetNextItemWidth(280);
        ImGui.InputTextWithHint("##npname", "Project name (optional)", ref _newProjectName, 100);

        // Create
        var canCreate = _newProjectSelected != null && !_newProjectBusy && !string.IsNullOrEmpty(_config.GuildId);
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create Project"))
            CreateProject();
        if (!canCreate) ImGui.EndDisabled();

        if (_newProjectBusy)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Creating…");
        }
        if (!string.IsNullOrEmpty(_newProjectError))
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _newProjectError);

        ImGui.Separator();
        ImGui.TextDisabled("Or paste a list (e.g. \"12x Iron Ore\" or \"Iron Ore x12\"):");
        ImGui.InputTextMultiline("##nplist", ref _newProjectList, 16384, new Vector2(280, 90));

        var canImport = !_newProjectBusy && !string.IsNullOrEmpty(_config.GuildId) && !string.IsNullOrWhiteSpace(_newProjectList);
        if (!canImport) ImGui.BeginDisabled();
        if (ImGui.Button("Create from list"))
            CreateProjectFromList();
        if (!canImport) ImGui.EndDisabled();
    }

    private void SearchNewProjectItems()
    {
        var query = _newProjectSearch.Trim();
        if (query.Length < 2) return;
        Task.Run(async () =>
        {
            try
            {
                var page = await _api.SearchItemsAsync(query, 1, 20);
                _newProjectResults = page?.Items ?? [];
                if (_newProjectResults.Count > 0) _newProjectSelected = _newProjectResults[0];
            }
            catch (Exception ex)
            {
                _newProjectError = $"Search failed: {ex.Message}";
            }
        });
    }

    private void CreateProject()
    {
        if (_newProjectSelected is null) return;
        var item = _newProjectSelected;
        var qty = _newProjectQty;
        var name = string.IsNullOrWhiteSpace(_newProjectName) ? null : _newProjectName.Trim();

        _newProjectBusy = true;
        _newProjectError = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                var result = await _api.CreateProjectAsync(_config.GuildId, item.Id, qty, name, CharacterName);
                if (result.Ok)
                {
                    // Reset the form and refresh the project list so the new one appears.
                    _showNewProject     = false;
                    _newProjectSearch   = string.Empty;
                    _newProjectResults  = [];
                    _newProjectSelected = null;
                    _newProjectQty      = 1;
                    _newProjectName     = string.Empty;
                    LoadProjects();
                }
                else
                {
                    _newProjectError = result.Error ?? "Could not create project.";
                }
            }
            catch (Exception ex)
            {
                _newProjectError = $"Create failed: {ex.Message}";
            }
            finally
            {
                _newProjectBusy = false;
            }
        });
    }

    // Parse list lines in either order, with or without an "x" marker. Patterns
    // are tried in order and the FIRST match wins, so "x"-marked forms beat
    // bare-number forms — that keeps names with embedded digits intact
    // (e.g. "Grade 4 Tincture x3" → qty 3, not 4). Skips blank lines and lines
    // with qty < 1. Returns the parsed (name, qty) pairs and the count of
    // unparseable lines.
    private static readonly Regex[] ListLineRegexes =
    [
        new(@"^\s*(?<name>.+?)\s*[xX×]\s*(?<qty>\d+)\s*$", RegexOptions.Compiled), // Iron Ore x12
        new(@"^\s*(?<qty>\d+)\s*[xX×]\s*(?<name>.+?)\s*$", RegexOptions.Compiled), // 12x Iron Ore
        new(@"^\s*(?<name>.+?)\s+(?<qty>\d+)\s*$",         RegexOptions.Compiled), // Iron Ore 12
        new(@"^\s*(?<qty>\d+)\s+(?<name>.+?)\s*$",         RegexOptions.Compiled), // 12 Iron Ore
    ];

    private static (List<(string name, int qty)> items, int skipped) ParseList(string text)
    {
        var items = new List<(string name, int qty)>();
        var skipped = 0;
        foreach (var raw in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var matched = false;
            foreach (var rx in ListLineRegexes)
            {
                var m = rx.Match(raw);
                if (!m.Success) continue;
                // A matched pattern with an unusable qty is intentionally dropped
                // (not retried against looser patterns).
                if (int.TryParse(m.Groups["qty"].Value, out var qty) && qty >= 1)
                {
                    items.Add((m.Groups["name"].Value.Trim(), qty));
                    matched = true;
                }
                break;
            }
            if (!matched) skipped++;
        }
        return (items, skipped);
    }

    private void CreateProjectFromList()
    {
        var (items, skipped) = ParseList(_newProjectList);
        if (items.Count == 0)
        {
            _newProjectError = "No valid lines (expected e.g. \"12x Iron Ore\" or \"Iron Ore x12\").";
            return;
        }
        var name = string.IsNullOrWhiteSpace(_newProjectName) ? "Imported project" : _newProjectName.Trim();

        _newProjectBusy = true;
        _newProjectError = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                var result = await _api.CreateProjectFromListAsync(_config.GuildId, name, items, CharacterName);
                if (result.Ok)
                {
                    var notes = new List<string>();
                    if (skipped > 0) notes.Add($"skipped {skipped} unparseable line(s)");
                    if (result.Unmatched != null && result.Unmatched.Count > 0) notes.Add($"couldn't find: {string.Join(", ", result.Unmatched)}");
                    _newProjectError = notes.Count > 0 ? "Created — " + string.Join("; ", notes) : string.Empty;

                    // Reset the form and refresh. Keep open only if there are notes to read.
                    _showNewProject     = notes.Count > 0;
                    _newProjectSearch   = string.Empty;
                    _newProjectResults  = [];
                    _newProjectSelected = null;
                    _newProjectQty      = 1;
                    _newProjectName     = string.Empty;
                    _newProjectList     = string.Empty;
                    LoadProjects();
                }
                else
                {
                    var msg = result.Error ?? "Could not create project.";
                    if (result.Unmatched != null && result.Unmatched.Count > 0)
                        msg += $" (couldn't find: {string.Join(", ", result.Unmatched)})";
                    _newProjectError = msg;
                }
            }
            catch (Exception ex)
            {
                _newProjectError = $"Import failed: {ex.Message}";
            }
            finally
            {
                _newProjectBusy = false;
            }
        });
    }

    private void LoadProjects()
    {
        if (string.IsNullOrEmpty(_config.GuildId))
        {
            _projectsError = "Guild ID not set — open Config and paste your Discord server ID.";
            return;
        }

        _projectsLoading = true;
        _projectsError   = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                _projects      = await _api.GetProjectsAsync(_config.GuildId);
                _projectsError = string.Empty;
                if (_projects.Count > 0)
                {
                    _selectedProjectIndex = 0;
                    LoadProjectDetail(_projects[0].Id);
                }
            }
            catch (Exception ex)
            {
                _projectsError = $"Failed to load projects: {ex.Message}";
            }
            finally
            {
                _projectsLoading = false;
            }
        });
    }

    private void LoadProjectDetail(int projectId)
    {
        Task.Run(async () =>
        {
            try
            {
                _projectDetail = await _api.GetProjectDetailAsync(projectId);
            }
            catch (Exception ex)
            {
                _projectsError = $"Failed to load tasks: {ex.Message}";
            }
        });
    }

    private void ClaimTask(int projectId, int taskId)
    {
        _claimInProgress = true;
        _claimError      = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                var updated = await _api.ClaimTaskAsync(projectId, taskId, CharacterName, _config.GuildId);
                if (updated is null)
                {
                    _claimError = "Task was already claimed — refresh to see latest state.";
                }
                else if (_projectDetail is not null)
                {
                    // Optimistically update the local row.
                    var idx = _projectDetail.Tasks.FindIndex(t => t.Id == updated.Id);
                    if (idx >= 0) _projectDetail.Tasks[idx] = updated;
                }
            }
            catch (Exception ex)
            {
                _claimError = $"Claim failed: {ex.Message}";
            }
            finally
            {
                _claimInProgress = false;
            }
        });
    }

    // A character-claim stores the character name as the assignee id, so a task is
    // "mine" when its assignee matches the current character. Discord-claimed tasks
    // store a Discord user id, which won't match — their controls stay hidden.
    private bool IsMyClaim(ApiTask task) =>
        task.AssigneeId is { Length: > 0 } id &&
        string.Equals(id, CharacterName, StringComparison.Ordinal);

    private void LogProgress(int projectId, int taskId, int amount)
    {
        _claimInProgress = true;
        _claimError      = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                var updated = await _api.LogProgressAsync(projectId, taskId, CharacterName, _config.GuildId, amount);
                if (updated is null)
                {
                    _claimError = "Couldn't log progress — you may no longer own this claim. Refresh.";
                }
                else if (_projectDetail is not null)
                {
                    var idx = _projectDetail.Tasks.FindIndex(t => t.Id == updated.Id);
                    if (idx >= 0) _projectDetail.Tasks[idx] = updated;
                    _progressAmounts.Remove(taskId);
                }
            }
            catch (Exception ex)
            {
                _claimError = $"Progress failed: {ex.Message}";
            }
            finally
            {
                _claimInProgress = false;
            }
        });
    }

    private void SetProgress(int projectId, int taskId, int qtyDone)
    {
        _claimInProgress = true;
        _claimError      = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                var updated = await _api.SetProgressAsync(projectId, taskId, CharacterName, _config.GuildId, qtyDone);
                if (updated is null)
                {
                    _claimError = "Couldn't update progress — you may no longer own this claim. Refresh.";
                }
                else if (_projectDetail is not null)
                {
                    var idx = _projectDetail.Tasks.FindIndex(t => t.Id == updated.Id);
                    if (idx >= 0) _projectDetail.Tasks[idx] = updated;
                    _progressAmounts.Remove(taskId);
                }
            }
            catch (Exception ex)
            {
                _claimError = $"Update failed: {ex.Message}";
            }
            finally
            {
                _claimInProgress = false;
            }
        });
    }

    private void CompleteTask(int projectId, int taskId)
    {
        _claimInProgress = true;
        _claimError      = string.Empty;

        Task.Run(async () =>
        {
            try
            {
                var updated = await _api.CompleteTaskAsync(projectId, taskId, CharacterName, _config.GuildId);
                if (updated is null)
                {
                    _claimError = "Couldn't mark complete — you may no longer own this claim. Refresh.";
                }
                else if (_projectDetail is not null)
                {
                    var idx = _projectDetail.Tasks.FindIndex(t => t.Id == updated.Id);
                    if (idx >= 0) _projectDetail.Tasks[idx] = updated;
                    _progressAmounts.Remove(taskId);
                }
            }
            catch (Exception ex)
            {
                _claimError = $"Complete failed: {ex.Message}";
            }
            finally
            {
                _claimInProgress = false;
            }
        });
    }

    private void ScanInventory()
    {
        _craftLoading = true;
        _craftError   = string.Empty;
        _craftable    = [];

        // Read game memory synchronously — Draw() runs on the framework update thread,
        // which is safe for game data access.
        Dictionary<int, int> aggregated;
        try
        {
            aggregated = InventoryReader.AggregatedBags(_includeSaddlebag);
        }
        catch (Exception ex)
        {
            _craftError   = $"Could not read inventory: {ex.Message}";
            _craftLoading = false;
            return;
        }

        if (aggregated.Count == 0)
        {
            _craftError   = "Inventory appears empty. Make sure you are logged in.";
            _craftLoading = false;
            return;
        }

        var invList = new List<(int id, int qty)>();
        foreach (var (id, qty) in aggregated) invList.Add((id, qty));

        Task.Run(async () =>
        {
            try
            {
                _craftable  = await _api.GetCraftableAsync(invList, _maxMissing);
                _craftError = string.Empty;
            }
            catch (Exception ex)
            {
                _craftError = $"Craftable fetch failed: {ex.Message}";
            }
            finally
            {
                _craftLoading = false;
                _craftScanned = true;
            }
        });
    }

    private void ExportCraftableToText()
    {
        var lines = new List<string>();
        foreach (var c in _craftable)
            if (c.Qty > 0) lines.Add($"{c.Qty}x {c.Name}");

        if (lines.Count == 0)
        {
            _craftExportStatus = "Nothing to export";
            return;
        }

        ImGui.SetClipboardText(string.Join("\n", lines));
        _craftExportStatus = $"Copied {lines.Count} items";
    }

    public void Dispose() { }
}
