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

    // Search state
    private string _searchQuery = "";
    private List<ItemSearchResult> _searchResults = new();
    private bool _isSearching = false;
    private string? _searchError = null;
    private int _currentPage = 1;
    private int _totalResults = 0;
    private const int PageSize = 20;

    // Sources modal state
    private ItemSearchResult? _selectedItem = null;
    private ItemSourcesResponse? _selectedSources = null;
    private bool _isLoadingSources = false;
    private string? _sourcesError = null;
    private bool _sourcesModalOpen = false;

    public SearchWindow(ApiClient api) : base("Item Search")
    {
        _api = api;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(1200, 800),
        };
    }

    public override void Draw()
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
        DrawSourcesModal();
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

        if (ImGui.BeginTable("##itemsTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Rarity", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();

            foreach (var item in _searchResults)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.PushID($"item_{item.Id}");
                if (ImGui.Selectable(item.Name, _selectedItem?.Id == item.Id, ImGuiSelectableFlags.SpanAllColumns))
                {
                    SelectItem(item);
                }
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

    private void DrawSourcesModal()
    {
        if (_selectedItem == null) return;

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.BeginPopupModal($"Sources: {_selectedItem.Name}##sources", ref _sourcesModalOpen))
        {
            if (_sourcesError != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
                ImGui.TextWrapped($"Error: {_sourcesError}");
                ImGui.PopStyleColor();
            }
            else if (_isLoadingSources)
            {
                ImGui.Text("Loading sources...");
            }
            else if (_selectedSources != null)
            {
                DrawSourcesList(_selectedSources);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 100);
            if (ImGui.Button("Close", new Vector2(90, 0)))
            {
                _selectedItem    = null;
                _selectedSources = null;
                _sourcesModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawSourcesList(ItemSourcesResponse sources)
    {
        if (sources.Sources.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No sources available for this item");
            return;
        }

        foreach (var source in sources.Sources)
        {
            switch (source)
            {
                case RecipeSource recipe:
                    DrawRecipeSource(recipe);
                    break;
                case VendorSource vendor:
                    DrawVendorSource(vendor);
                    break;
                case GatheringSource gathering:
                    DrawGatheringSource(gathering);
                    break;
                case SpecialShopSource specialShop:
                    DrawSpecialShopSource(specialShop);
                    break;
                case CompanyCraftSource companyCraft:
                    DrawCompanyCraftSource(companyCraft);
                    break;
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
        {
            ImGui.BulletText($"{ing.ItemName} x{ing.Qty}");
        }
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
        {
            ImGui.BulletText($"{ing.ItemName} x{ing.Qty}");
        }
        ImGui.Unindent();
    }

    private void SelectItem(ItemSearchResult item)
    {
        _selectedItem     = item;
        _selectedSources  = null;
        _isLoadingSources = true;
        _sourcesError     = null;
        _sourcesModalOpen = true;
        ImGui.OpenPopup($"Sources: {item.Name}##sources");
        _ = LoadItemSources(item.Id);
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

    private async Task LoadItemSources(int itemId)
    {
        try
        {
            _selectedSources = await _api.GetItemSourcesAsync(itemId);
            _sourcesError = null;
        }
        catch (Exception ex)
        {
            _sourcesError = ex.Message;
            _selectedSources = null;
        }
        finally
        {
            _isLoadingSources = false;
        }
    }

    public void Dispose() { }
}
