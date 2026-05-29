using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace QiqirnCompanion.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record ApiProject(
    [property: JsonPropertyName("id")]           int    Id,
    [property: JsonPropertyName("name")]         string Name,
    [property: JsonPropertyName("status")]       string Status,
    [property: JsonPropertyName("targetItemId")] int    TargetItemId,
    [property: JsonPropertyName("targetQty")]    int    TargetQty
);

public record ApiTask(
    [property: JsonPropertyName("id")]           int     Id,
    [property: JsonPropertyName("itemName")]     string  ItemName,
    [property: JsonPropertyName("qtyNeeded")]    int     QtyNeeded,
    [property: JsonPropertyName("qtyDone")]      int     QtyDone,
    [property: JsonPropertyName("status")]       string  Status,
    [property: JsonPropertyName("assigneeId")]   string? AssigneeId,
    [property: JsonPropertyName("assigneeName")] string? AssigneeName
);

public record ApiProjectDetail(
    [property: JsonPropertyName("project")] ApiProject      Project,
    [property: JsonPropertyName("tasks")]   List<ApiTask>   Tasks
);

public record CraftableItem(
    [property: JsonPropertyName("itemId")]   int    ItemId,
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("qty")]      int    Qty,
    [property: JsonPropertyName("minNQ")]    int?   MinNQ,
    [property: JsonPropertyName("velocity")] double Velocity
);

public record ItemSearchResult(
    [property: JsonPropertyName("id")]        int  Id,
    [property: JsonPropertyName("name")]      string Name,
    [property: JsonPropertyName("hasRecipe")] bool HasRecipe,
    [property: JsonPropertyName("rarity")]    int  Rarity
);

public record ItemsPageResponse(
    [property: JsonPropertyName("items")]     List<ItemSearchResult> Items,
    [property: JsonPropertyName("total")]     int Total,
    [property: JsonPropertyName("page")]      int Page,
    [property: JsonPropertyName("pageSize")]  int PageSize
);

public abstract record ItemSource(
    [property: JsonPropertyName("type")] string Type
);

public record RecipeSource(
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("jobId")]       int JobId,
    [property: JsonPropertyName("jobName")]     string JobName,
    [property: JsonPropertyName("level")]       int Level,
    [property: JsonPropertyName("ingredients")] List<(int ItemId, string ItemName, int Qty)> Ingredients,
    [property: JsonPropertyName("outputQty")]   int OutputQty
) : ItemSource(Type);

public record VendorSource(
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("npcId")]   int NpcId,
    [property: JsonPropertyName("npcName")] string NpcName,
    [property: JsonPropertyName("price")]   int Price
) : ItemSource(Type);

public record GatheringSource(
    [property: JsonPropertyName("type")]  string Type,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("timed")] bool Timed
) : ItemSource(Type);

public record SpecialShopSource(
    [property: JsonPropertyName("type")]       string Type,
    [property: JsonPropertyName("currency")]   string Currency,
    [property: JsonPropertyName("currencyId")] int CurrencyId,
    [property: JsonPropertyName("cost")]       int Cost
) : ItemSource(Type);

public record CompanyCraftSource(
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("craftName")]   string CraftName,
    [property: JsonPropertyName("ingredients")] List<(int ItemId, string ItemName, int Qty)> Ingredients
) : ItemSource(Type);

public record ItemSourcesResponse(
    [property: JsonPropertyName("itemId")]   int ItemId,
    [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonPropertyName("sources")]  List<ItemSource> Sources
);

// ── Client ────────────────────────────────────────────────────────────────────

public class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("User-Agent", "QiqirnCompanion/1.0");
    }

    /// <summary>List all open projects for a guild.</summary>
    public async Task<List<ApiProject>> GetProjectsAsync(string guildId)
    {
        var res = await _http.GetAsync($"api/projects?guild={Uri.EscapeDataString(guildId)}");
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<JsonElement>(_json);
        return JsonSerializer.Deserialize<List<ApiProject>>(
            data.GetProperty("projects").GetRawText(), _json) ?? [];
    }

    /// <summary>Get a project and its tasks by ID.</summary>
    public async Task<ApiProjectDetail?> GetProjectDetailAsync(int id)
    {
        var res = await _http.GetAsync($"api/projects/{id}");
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<ApiProjectDetail>(_json);
    }

    /// <summary>Claim a task as a character. Returns the updated task, or null if already claimed.</summary>
    public async Task<ApiTask?> ClaimTaskAsync(int projectId, int taskId, string characterName, string guildId)
    {
        var body    = new { projectId, taskId, characterName, guildId };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var res     = await _http.PostAsync("api/plugin/claim", content);
        if (res.StatusCode == System.Net.HttpStatusCode.Conflict) return null;
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<JsonElement>(_json);
        return JsonSerializer.Deserialize<ApiTask>(data.GetProperty("task").GetRawText(), _json);
    }

    /// <summary>Get craftable items for a given inventory (list of itemId + qty pairs).</summary>
    public async Task<List<CraftableItem>> GetCraftableAsync(List<(int id, int qty)> inv)
    {
        var invJson = JsonSerializer.Serialize(inv.ConvertAll(x => new { id = x.id, qty = x.qty }));
        var encoded = Uri.EscapeDataString(invJson);
        var res     = await _http.GetAsync($"api/plugin/craftable?inv={encoded}");
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<JsonElement>(_json);
        return JsonSerializer.Deserialize<List<CraftableItem>>(
            data.GetProperty("craftable").GetRawText(), _json) ?? [];
    }

    /// <summary>Search for items by name with pagination.</summary>
    public async Task<ItemsPageResponse?> SearchItemsAsync(string query, int page = 1, int pageSize = 20)
    {
        var encoded = Uri.EscapeDataString(query);
        var res     = await _http.GetAsync($"api/plugin/items?q={encoded}&page={page}&pageSize={pageSize}");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<ItemsPageResponse>(_json);
    }

    /// <summary>Get all ways to obtain an item (recipes, vendors, gathering, etc).</summary>
    public async Task<ItemSourcesResponse?> GetItemSourcesAsync(int itemId)
    {
        var res = await _http.GetAsync($"api/plugin/item-sources?id={itemId}");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<ItemSourcesResponse>(_json);
    }

    public void Dispose() => _http.Dispose();
}
