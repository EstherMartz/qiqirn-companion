using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using QiqirnCompanion.Planner;
using System;
using System.Text.RegularExpressions;

namespace QiqirnCompanion.Services;

/// <summary>
/// Watches for retainer market-sale chat lines and auto-logs each into the planner
/// (treasury + matching plan item). Self-contained — no third-party plugin needed.
/// </summary>
public sealed class SalesTracker : IDisposable
{
    private readonly IChatGui      _chat;
    private readonly IDataManager  _data;
    private readonly Configuration _config;

    // Matches the gil total in "...has sold for 12,345 gil." (EN client).
    private static readonly Regex GilRe = new(@"([\d,]+)\s*gil", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Most recent auto-logged sale this session, for a small UI hint.</summary>
    public (string Name, long Gil)? LastLogged { get; private set; }

    public SalesTracker(IChatGui chat, IDataManager data, Configuration config)
    {
        _chat   = chat;
        _data   = data;
        _config = config;
        _chat.ChatMessage += OnChat;
    }

    private void OnChat(IHandleableChatMessage msg)
    {
        if ((int)msg.LogKind != (int)XivChatType.RetainerSale) return;
        if (!_config.AutoLogRetainerSales) return;

        var message = msg.Message;

        // Item id from the first item link in the message.
        uint itemId = 0;
        foreach (var p in message.Payloads)
        {
            if (p is ItemPayload ip) { itemId = ip.ItemId; break; }
        }

        // Gil total from the message text.
        var m = GilRe.Match(message.TextValue);
        if (!m.Success) return;
        if (!long.TryParse(m.Groups[1].Value.Replace(",", ""), out var gil) || gil <= 0) return;

        var name = ResolveName(itemId);

        var data = _config.Planner ??= SeedPlanner.CreateDefault();
        var plannerId = MatchPlanItem(data, name);
        PlannerLogic.LogGil(data, gil, plannerId, string.IsNullOrEmpty(name) ? "Retainer sale" : name, source: "sale");
        _config.Save();

        LastLogged = (string.IsNullOrEmpty(name) ? "(item)" : name, gil);
    }

    private string ResolveName(uint itemId)
    {
        if (itemId == 0) return "";
        try
        {
            var row = _data.GetExcelSheet<Item>()?.GetRowOrDefault(itemId);
            return row?.Name.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string? MatchPlanItem(PlannerData data, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = name.Trim().ToLowerInvariant();
        foreach (var lane in Lanes.Order)
        {
            if (!data.Lanes.TryGetValue(lane, out var items)) continue;
            foreach (var it in items)
                if (it.Name.Trim().ToLowerInvariant() == key) return it.Id;
        }
        return null;
    }

    public void Dispose() => _chat.ChatMessage -= OnChat;
}
