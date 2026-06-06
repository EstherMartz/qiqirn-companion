using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QiqirnCompanion.Models;

// ── Persisted (config) ──────────────────────────────────────────────────────
public class ImportedListItem
{
    public int  ItemId { get; set; }
    public int  Qty    { get; set; }
    public bool Hq     { get; set; }
}

public class ImportedList
{
    public string Id         { get; set; } = "";   // local guid
    public string Name       { get; set; } = "";
    public long   ImportedAt { get; set; }          // unix ms
    public List<ImportedListItem> Items { get; set; } = new();
}

// ── API response (POST /api/plugin/craft-breakdown) ─────────────────────────
public record BreakdownFinalItem(
    [property: JsonPropertyName("itemId")]      int     ItemId,
    [property: JsonPropertyName("itemName")]    string  ItemName,
    [property: JsonPropertyName("qty")]         int     Qty,
    [property: JsonPropertyName("isHq")]        bool    IsHq,
    [property: JsonPropertyName("job")]         string? Job,
    [property: JsonPropertyName("recipeLevel")] int?    RecipeLevel,
    [property: JsonPropertyName("stars")]       int?    Stars
);

public record BreakdownIngredient(
    [property: JsonPropertyName("itemId")]       int          ItemId,
    [property: JsonPropertyName("itemName")]     string       ItemName,
    [property: JsonPropertyName("requiredQty")]  int          RequiredQty,
    [property: JsonPropertyName("source")]       string       Source,
    [property: JsonPropertyName("craftedByJob")] string?      CraftedByJob,
    [property: JsonPropertyName("recipeLevel")]  int?         RecipeLevel,
    [property: JsonPropertyName("usedToCraft")]  List<string> UsedToCraft,
    [property: JsonPropertyName("depth")]        int?         Depth,
    [property: JsonPropertyName("canHq")]        bool?        CanHq
);

public record ListBreakdown(
    [property: JsonPropertyName("finalItems")]  List<BreakdownFinalItem>  FinalItems,
    [property: JsonPropertyName("ingredients")] List<BreakdownIngredient> Ingredients
);
