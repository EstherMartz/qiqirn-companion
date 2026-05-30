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

public record TaskMeta(
    [property: JsonPropertyName("partKey")]    string? PartKey,
    [property: JsonPropertyName("phaseIndex")] int?    PhaseIndex,
    [property: JsonPropertyName("job")]        string? Job,
    [property: JsonPropertyName("world")]      string? World
);

public record ApiTask(
    [property: JsonPropertyName("id")]           int      Id,
    [property: JsonPropertyName("itemId")]       int      ItemId,
    [property: JsonPropertyName("itemName")]     string   ItemName,
    [property: JsonPropertyName("qtyNeeded")]    int      QtyNeeded,
    [property: JsonPropertyName("qtyDone")]      int      QtyDone,
    [property: JsonPropertyName("status")]       string   Status,
    [property: JsonPropertyName("source")]       string?  Source,
    [property: JsonPropertyName("meta")]         TaskMeta? Meta,
    [property: JsonPropertyName("assigneeId")]   string?  AssigneeId,
    [property: JsonPropertyName("assigneeName")] string?  AssigneeName
);

public record ApiProjectDetail(
    [property: JsonPropertyName("project")]   ApiProject                 Project,
    [property: JsonPropertyName("tasks")]     List<ApiTask>              Tasks,
    [property: JsonPropertyName("userNames")] Dictionary<string, string>? UserNames
);

public record CraftIngredient(
    [property: JsonPropertyName("itemId")] int    ItemId,
    [property: JsonPropertyName("name")]   string Name,
    [property: JsonPropertyName("needed")] int    Needed,
    [property: JsonPropertyName("have")]   int    Have
);

public record CraftableItem(
    [property: JsonPropertyName("itemId")]        int    ItemId,
    [property: JsonPropertyName("name")]          string Name,
    [property: JsonPropertyName("qty")]           int    Qty,
    [property: JsonPropertyName("missingCount")]  int    MissingCount,
    [property: JsonPropertyName("completeness")]  double Completeness,
    [property: JsonPropertyName("minNQ")]         int?   MinNQ,
    [property: JsonPropertyName("velocity")]      double Velocity,
    [property: JsonPropertyName("cheapestWorld")] string? CheapestWorld,
    [property: JsonPropertyName("cheapestPrice")] int?   CheapestPrice,
    [property: JsonPropertyName("ingredients")]   List<CraftIngredient>? Ingredients
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

public record IngredientItem(
    [property: JsonPropertyName("itemId")]   int    ItemId,
    [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonPropertyName("qty")]      int    Qty
);

public record RecipeSource(
    string                               Type,
    [property: JsonPropertyName("jobId")]       int                  JobId,
    [property: JsonPropertyName("jobName")]     string               JobName,
    [property: JsonPropertyName("level")]       int                  Level,
    [property: JsonPropertyName("ingredients")] List<IngredientItem> Ingredients,
    [property: JsonPropertyName("outputQty")]   int                  OutputQty
) : ItemSource(Type);

public record VendorSource(
    string                               Type,
    [property: JsonPropertyName("npcId")]   int    NpcId,
    [property: JsonPropertyName("npcName")] string NpcName,
    [property: JsonPropertyName("price")]   int    Price
) : ItemSource(Type);

public record GatheringSource(
    string                               Type,
    [property: JsonPropertyName("level")] int  Level,
    [property: JsonPropertyName("timed")] bool Timed
) : ItemSource(Type);

public record SpecialShopSource(
    string                               Type,
    [property: JsonPropertyName("currency")]   string Currency,
    [property: JsonPropertyName("currencyId")] int    CurrencyId,
    [property: JsonPropertyName("cost")]       int    Cost
) : ItemSource(Type);

public record CompanyCraftSource(
    string                               Type,
    [property: JsonPropertyName("craftName")]   string               CraftName,
    [property: JsonPropertyName("ingredients")] List<IngredientItem> Ingredients
) : ItemSource(Type);

/// <summary>
/// Polymorphic deserializer for <see cref="ItemSource"/>. System.Text.Json can't
/// instantiate the abstract base, so we dispatch on the backend's "type"
/// discriminator to the concrete record. Discriminator strings come from
/// api/plugin-item-sources.mjs (note "gather", "special_shop", "company_craft").
/// Unrecognized types (incl. the "unknown" placeholder) deserialize to null and
/// are skipped by the UI.
/// </summary>
public sealed class ItemSourceConverter : JsonConverter<ItemSource>
{
    public override ItemSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        var raw = root.GetRawText();
        return type switch
        {
            "recipe"        => JsonSerializer.Deserialize<RecipeSource>(raw, options),
            "vendor"        => JsonSerializer.Deserialize<VendorSource>(raw, options),
            "gather"        => JsonSerializer.Deserialize<GatheringSource>(raw, options),
            "special_shop"  => JsonSerializer.Deserialize<SpecialShopSource>(raw, options),
            "company_craft" => JsonSerializer.Deserialize<CompanyCraftSource>(raw, options),
            _               => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, ItemSource value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

public record MarketSummary(
    [property: JsonPropertyName("velocity")]      double  Velocity,
    [property: JsonPropertyName("listingCount")]  int     ListingCount,
    [property: JsonPropertyName("minNQ")]         int?    MinNQ,
    [property: JsonPropertyName("cheapestWorld")] string? CheapestWorld,
    [property: JsonPropertyName("cheapestPrice")] int?    CheapestPrice
);

public record ItemSourcesResponse(
    [property: JsonPropertyName("itemId")]   int ItemId,
    [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonPropertyName("sources")]  List<ItemSource> Sources,
    [property: JsonPropertyName("market")]   MarketSummary? Market
);

public record TradingQueryRow(
    [property: JsonPropertyName("id")]           int    Id,
    [property: JsonPropertyName("name")]         string Name,
    [property: JsonPropertyName("sc")]           int    Sc,
    [property: JsonPropertyName("unitPrice")]    double UnitPrice,
    [property: JsonPropertyName("averagePrice")] double AveragePrice,
    [property: JsonPropertyName("dealPct")]      int    DealPct,
    [property: JsonPropertyName("velocity")]     double Velocity,
    [property: JsonPropertyName("gilFlow")]      double GilFlow,
    [property: JsonPropertyName("hq")]           bool   Hq,
    [property: JsonPropertyName("cheapestWorld")] string? CheapestWorld,
    [property: JsonPropertyName("cheapestPrice")] double? CheapestPrice,
    [property: JsonPropertyName("materialCost")] double? MaterialCost,
    [property: JsonPropertyName("profit")]       double? Profit,
    [property: JsonPropertyName("gilPerDay")]    double? GilPerDay
);

public record TradingQueryResponse(
    [property: JsonPropertyName("rows")]   List<TradingQueryRow> Rows,
    [property: JsonPropertyName("total")]  int                   Total,
    [property: JsonPropertyName("preset")] string?               Preset,
    [property: JsonPropertyName("scope")]  string                Scope,
    [property: JsonPropertyName("mode")]   string?               Mode
);

public record TradingPreset(
    [property: JsonPropertyName("id")]       string Id,
    [property: JsonPropertyName("label")]    string Label,
    [property: JsonPropertyName("category")] string Category
);

public record TradingPresetsResponse(
    [property: JsonPropertyName("presets")] List<TradingPreset> Presets
);

// ── Inventory cleanup ───────────────────────────────────────────────────────

public record CleanupEntry(
    [property: JsonPropertyName("itemId")]    int          ItemId,
    [property: JsonPropertyName("name")]      string       Name,
    [property: JsonPropertyName("qty")]       int          Qty,
    [property: JsonPropertyName("isHq")]      bool         IsHq,
    [property: JsonPropertyName("locations")] List<string>? Locations
);

public record CraftUse(
    [property: JsonPropertyName("itemId")] int    ItemId,
    [property: JsonPropertyName("name")]   string Name,
    [property: JsonPropertyName("amount")] int    Amount
);

public record CraftMissing(
    [property: JsonPropertyName("itemId")]      int    ItemId,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("amount")]      int    Amount,
    [property: JsonPropertyName("mbUnitPrice")] double MbUnitPrice
);

public record CraftOpportunity(
    [property: JsonPropertyName("outputItemId")]      int               OutputItemId,
    [property: JsonPropertyName("outputName")]        string            OutputName,
    [property: JsonPropertyName("outputUnitPrice")]   double            OutputUnitPrice,
    [property: JsonPropertyName("netProfit")]         double            NetProfit,
    [property: JsonPropertyName("usedFromInventory")] List<CraftUse>?    UsedFromInventory,
    [property: JsonPropertyName("missingIngredients")]List<CraftMissing>? MissingIngredients
);

public record RunnerUp(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("value")]  double Value
);

public record CleanupRow(
    [property: JsonPropertyName("entry")]          CleanupEntry          Entry,
    [property: JsonPropertyName("vendorRevenue")]  double                VendorRevenue,
    [property: JsonPropertyName("mbRevenue")]      double                MbRevenue,
    [property: JsonPropertyName("mbListingCount")] int                   MbListingCount,
    [property: JsonPropertyName("mbScope")]        string?               MbScope,
    [property: JsonPropertyName("bestCraft")]      CraftOpportunity?     BestCraft,
    [property: JsonPropertyName("otherCrafts")]    List<CraftOpportunity>? OtherCrafts,
    [property: JsonPropertyName("bucket")]         string                Bucket,
    [property: JsonPropertyName("runnerUp")]       RunnerUp?             RunnerUp
);

public record CleanupSummary(
    [property: JsonPropertyName("craftCount")]   int    CraftCount,
    [property: JsonPropertyName("sellMbCount")]  int    SellMbCount,
    [property: JsonPropertyName("vendorCount")]  int    VendorCount,
    [property: JsonPropertyName("discardCount")] int    DiscardCount,
    [property: JsonPropertyName("vendorTotal")]  double VendorTotal,
    [property: JsonPropertyName("mbTotal")]      double MbTotal
);

public record CleanupResponse(
    [property: JsonPropertyName("craft")]   List<CleanupRow> Craft,
    [property: JsonPropertyName("sellMb")]  List<CleanupRow> SellMb,
    [property: JsonPropertyName("vendor")]  List<CleanupRow> Vendor,
    [property: JsonPropertyName("discard")] List<CleanupRow> Discard,
    [property: JsonPropertyName("summary")] CleanupSummary   Summary
);

// ── Client ────────────────────────────────────────────────────────────────────

public class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ItemSourceConverter() },
    };

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

    /// <summary>Get craftable items for a given inventory (list of itemId + qty pairs).
    /// maxMissing &gt; 0 also returns near-complete crafts missing up to N ingredient types.</summary>
    public async Task<List<CraftableItem>> GetCraftableAsync(List<(int id, int qty)> inv, int maxMissing = 0)
    {
        var invJson = JsonSerializer.Serialize(inv.ConvertAll(x => new { id = x.id, qty = x.qty }));
        var encoded = Uri.EscapeDataString(invJson);
        var res     = await _http.GetAsync($"api/plugin/craftable?inv={encoded}&maxMissing={maxMissing}");
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

    /// <summary>Run a trading query preset and return market opportunity rows.</summary>
    public async Task<TradingQueryResponse?> RunTradingQueryAsync(string presetId, string? world = null)
    {
        var url = $"api/plugin/trading/query?preset={Uri.EscapeDataString(presetId)}";
        if (!string.IsNullOrEmpty(world))
            url += $"&world={Uri.EscapeDataString(world)}";
        var res = await _http.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<TradingQueryResponse>(_json);
    }

    /// <summary>Fetch the backend's trading-preset catalog (id/label/category).</summary>
    public async Task<List<TradingPreset>?> GetTradingPresetsAsync()
    {
        var res = await _http.GetAsync("api/plugin/trading/query?list=1");
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<TradingPresetsResponse>(_json);
        return data?.Presets;
    }

    /// <summary>Classify live inventory into craft/sell/vendor/discard buckets.</summary>
    public async Task<CleanupResponse?> GetCleanupAsync(List<(int id, int qty, bool hq)> inv)
    {
        var invJson = JsonSerializer.Serialize(inv.ConvertAll(x => new { id = x.id, qty = x.qty, hq = x.hq }));
        var encoded = Uri.EscapeDataString(invJson);
        var res     = await _http.GetAsync($"api/plugin/cleanup?inv={encoded}");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CleanupResponse>(_json);
    }

    public void Dispose() => _http.Dispose();
}
