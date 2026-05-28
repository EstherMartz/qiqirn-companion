using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly ApiClient     _api;
    private readonly IPlayerState  _playerState;

    // ── Projects tab state ────────────────────────────────────────────────────
    private List<ApiProject>    _projects            = [];
    private int                 _selectedProjectIndex = 0;
    private ApiProjectDetail?   _projectDetail;
    private bool                _projectsLoading     = false;
    private string              _projectsError       = string.Empty;
    private bool                _claimInProgress     = false;
    private string              _claimError          = string.Empty;

    // ── Crafting tab state ────────────────────────────────────────────────────
    private List<CraftableItem> _craftable       = [];
    private bool                _craftLoading    = false;
    private string              _craftError      = string.Empty;
    private bool                _includeSaddlebag = false;

    public MainWindow(Configuration config, ApiClient api, IPlayerState playerState)
        : base("Qiqirn Companion##main", ImGuiWindowFlags.None)
    {
        _config      = config;
        _api         = api;
        _playerState = playerState;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(900, 700),
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

        DrawProjectsTab();
        DrawCraftingTab();

        ImGui.EndTabBar();
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

        ImGui.Separator();

        if (_projectDetail is not null)
        {
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

    private void DrawTasksTable(ApiProjectDetail detail)
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders     |
            ImGuiTableFlags.RowBg       |
            ImGuiTableFlags.ScrollY     |
            ImGuiTableFlags.SizingFixedFit;

        if (!ImGui.BeginTable("##tasks", 5, flags, new Vector2(0, 220))) return;

        ImGui.TableSetupColumn("Item",     ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty",      ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Status",   ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Assignee", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("",         ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableHeadersRow();

        foreach (var task in detail.Tasks)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(task.ItemName);

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
            var assignee = task.AssigneeName ?? task.AssigneeId ?? "—";
            ImGui.TextUnformatted(assignee);

            ImGui.TableSetColumnIndex(4);
            if (task.Status == "open")
            {
                ImGui.PushID(task.Id);
                if (_claimInProgress) ImGui.BeginDisabled();
                if (ImGui.SmallButton("Claim"))
                    ClaimTask(detail.Project.Id, task.Id);
                if (_claimInProgress) ImGui.EndDisabled();
                ImGui.PopID();
            }
        }

        ImGui.EndTable();
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

        if (_craftable.Count == 0 && !_craftLoading)
            ImGui.TextDisabled("Click 'Scan Inventory' to see what you can craft.");
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
            ImGuiTableFlags.SizingFixedFit;

        if (!ImGui.BeginTable("##craftable", 4, flags, new Vector2(0, 260))) return;

        ImGui.TableSetupColumn("Item",         ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Can Make",     ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Min Price NQ", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Sales/day",    ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableHeadersRow();

        foreach (var item in _craftable)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            // Click item name to copy to clipboard.
            if (ImGui.Selectable(item.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                ImGui.SetClipboardText(item.Name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Click to copy item name");

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(item.Qty.ToString());

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(item.MinNQ.HasValue ? item.MinNQ.Value.ToString("N0") : "—");

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(item.Velocity > 0 ? item.Velocity.ToString("F1") : "—");
        }

        ImGui.EndTable();
    }

    // ── Async helpers ─────────────────────────────────────────────────────────

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
                _craftable  = await _api.GetCraftableAsync(invList);
                _craftError = string.Empty;
            }
            catch (Exception ex)
            {
                _craftError = $"Craftable fetch failed: {ex.Message}";
            }
            finally
            {
                _craftLoading = false;
            }
        });
    }

    public void Dispose() { }
}
