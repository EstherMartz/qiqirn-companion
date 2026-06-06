using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using QiqirnCompanion.Models;

namespace QiqirnCompanion.Services;

/// <summary>
/// Decodes the web app's "qq:list:v1:&lt;base64url&gt;" share code into an
/// <see cref="ImportedList"/>. Mirrors ffxiv-helper's src/features/craftLists/listCode.ts
/// encoder: payload JSON is { n: name, i: [[itemId, qty, hqFlag], ...] }.
/// Returns null on any malformed input (never throws).
/// </summary>
public static class ListCodec
{
    private const string Prefix = "qq:list:v1:";

    public static ImportedList? Decode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        try
        {
            var b64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4) { case 2: b64 += "=="; break; case 3: b64 += "="; break; }
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("n", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("i", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array) return null;

            var items = new List<ImportedListItem>();
            foreach (var tup in itemsEl.EnumerateArray())
            {
                if (tup.ValueKind != JsonValueKind.Array || tup.GetArrayLength() < 2) return null;
                var id  = tup[0].GetInt32();
                var qty = tup[1].GetInt32();
                var hq  = tup.GetArrayLength() >= 3 && tup[2].GetInt32() == 1;
                if (id <= 0 || qty < 1) return null;
                items.Add(new ImportedListItem { ItemId = id, Qty = qty, Hq = hq });
            }
            if (items.Count == 0) return null;

            return new ImportedList
            {
                Id    = Guid.NewGuid().ToString("N")[..12],
                Name  = nameEl.GetString() ?? "Imported list",
                Items = items,
            };
        }
        catch
        {
            return null;
        }
    }
}
