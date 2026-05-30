using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace QiqirnCompanion.Windows;

public class SearchWindow : Window, IDisposable
{
    private readonly ApiClient _api;
    private readonly ItemInfoWindow _itemInfo;

    // Search state
    private string _searchQuery = "";
    private List<ItemSearchResult> _searchResults = new();
    private bool _isSearching = false;
    private string? _searchError = null;
    private int _currentPage = 1;
    private int _totalResults = 0;
    private const int PageSize = 20;

    // Set by RunQuery; consumed once when the next search completes, to jump
    // straight to ItemInfoWindow on an exact name match.
    private string? _pendingExactQuery = null;

    public SearchWindow(ApiClient api, ItemInfoWindow itemInfo) : base("Item Search")
    {
        _api = api;
        _itemInfo = itemInfo;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(1200, 800),
        };
    }

    public override void Draw() => DrawContent();

    public void DrawContent()
    {
        DrawSearchBar();
        ImGui.Separator();

        if (_searchError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
            ImGui.TextWrapped($"Error: {_searchError}");
            ImGui.PopStyleColor();
            return;
        }

        if (_searchQuery.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Enter at least 2 characters to search");
            return;
        }

        if (_isSearching)
        {
            ImGui.Text("Searching...");
            return;
        }

        DrawResults();
    }

    private void DrawSearchBar()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##search", "Search items by name...", ref _searchQuery, 256))
        {
            _currentPage = 1;
            if (_searchQuery.Length >= 2)
            {
                _ = PerformSearch();
            }
            else
            {
                _searchResults.Clear();
                _totalResults = 0;
            }
        }

        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 100);
        if (_totalResults > 0)
        {
            ImGui.TextDisabled($"{_searchResults.Count} / {_totalResults}");
        }
    }

    private void DrawResults()
    {
        if (_searchResults.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No results found");
            return;
        }

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable;
        if (ImGui.BeginTable("##itemsTable", 3, tableFlags))
        {
            ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Rarity", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();

            SortIfNeeded();

            foreach (var item in _searchResults)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.PushID($"item_{item.Id}");
                if (ImGui.Selectable(item.Name, false, ImGuiSelectableFlags.SpanAllColumns))
                {
                    SelectItem(item);
                }
                // Single-click opens sources (above); add link/menu, no copy-on-click.
                ItemInteractions.HandleRow((uint)item.Id, item.Name, false, copyOnClick: false);
                ImGui.PopID();

                ImGui.TableSetColumnIndex(1);
                if (item.Rarity > 0)
                {
                    var color = item.Rarity switch
                    {
                        1 => new Vector4(0.7f, 0.7f, 0.7f, 1),
                        2 => new Vector4(0.2f, 0.8f, 0.2f, 1),
                        3 => new Vector4(0.2f, 0.6f, 1, 1),
                        4 => new Vector4(1, 0.5f, 0, 1),
                        _ => new Vector4(1, 1, 1, 1),
                    };
                    ImGui.TextColored(color, item.Rarity.ToString());
                }

                ImGui.TableSetColumnIndex(2);
                if (item.HasRecipe)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "✓");
                }
            }

            ImGui.EndTable();
        }

        DrawPagination();
    }

    private void SortIfNeeded()
    {
        var specs = ImGui.TableGetSortSpecs();
        if (!specs.SpecsDirty || specs.SpecsCount == 0) return;

        var spec = specs.Specs;
        var asc = spec.SortDirection == ImGuiSortDirection.Ascending;
        Comparison<ItemSearchResult> cmp = spec.ColumnIndex switch
        {
            0 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            1 => (a, b) => a.Rarity.CompareTo(b.Rarity),
            2 => (a, b) => a.HasRecipe.CompareTo(b.HasRecipe),
            _ => (a, b) => 0,
        };
        _searchResults.Sort((a, b) => asc ? cmp(a, b) : -cmp(a, b));
        specs.SpecsDirty = false;
    }

    private void DrawPagination()
    {
        var maxPages = (_totalResults + PageSize - 1) / PageSize;
        if (maxPages <= 1) return;

        ImGui.Spacing();
        ImGui.Separator();

        var buttonWidth = 40f;
        var availWidth = ImGui.GetContentRegionAvail().X;
        var totalButtonWidth = (buttonWidth * 4) + (ImGui.GetStyle().ItemSpacing.X * 3);
        var centerOffset = (availWidth - totalButtonWidth) / 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerOffset);

        if (ImGui.Button("<<", new Vector2(buttonWidth, 0)) && _currentPage > 1)
        {
            _currentPage = 1;
            _ = PerformSearch();
        }

        ImGui.SameLine();
        if (ImGui.Button("<", new Vector2(buttonWidth, 0)) && _currentPage > 1)
        {
            _currentPage--;
            _ = PerformSearch();
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5);
        ImGui.Text($"Page {_currentPage}/{maxPages}");
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5);

        ImGui.SameLine();
        if (ImGui.Button(">", new Vector2(buttonWidth, 0)) && _currentPage < maxPages)
        {
            _currentPage++;
            _ = PerformSearch();
        }

        ImGui.SameLine();
        if (ImGui.Button(">>", new Vector2(buttonWidth, 0)) && _currentPage < maxPages)
        {
            _currentPage = maxPages;
            _ = PerformSearch();
        }
    }

    private void SelectItem(ItemSearchResult item) => _itemInfo.Show((uint)item.Id, item.Name);

    /// <summary>
    /// Run a search from outside the window (the <c>/qiqirn &lt;item&gt;</c> command).
    /// On completion, an exact name match jumps straight to <see cref="ItemInfoWindow"/>.
    /// </summary>
    public void RunQuery(string query)
    {
        _searchQuery = query;
        _currentPage = 1;
        if (query.Length >= 2)
        {
            _pendingExactQuery = query;
            _ = PerformSearch();
        }
        else
        {
            _pendingExactQuery = null;
            _searchResults.Clear();
            _totalResults = 0;
        }
    }

    private async Task PerformSearch()
    {
        if (_searchQuery.Length < 2) return;

        _isSearching = true;
        _searchError = null;

        try
        {
            var response = await _api.SearchItemsAsync(_searchQuery, _currentPage, PageSize);
            if (response != null)
            {
                _searchResults = response.Items;
                _totalResults = response.Total;

                if (_pendingExactQuery is { } exact)
                {
                    _pendingExactQuery = null;
                    var matches = _searchResults.FindAll(r =>
                        string.Equals(r.Name.Trim(), exact, StringComparison.OrdinalIgnoreCase));
                    if (matches.Count == 1)
                        _itemInfo.Show((uint)matches[0].Id, matches[0].Name);
                }
            }
        }
        catch (Exception ex)
        {
            _searchError = ex.Message;
            _searchResults.Clear();
            _totalResults = 0;
        }
        finally
        {
            _isSearching = false;
        }
    }

    public void Dispose() { }
}
