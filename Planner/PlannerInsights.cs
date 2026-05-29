using System;
using System.Collections.Generic;
using System.Linq;

namespace QiqirnCompanion.Planner;

/// <summary>
/// Derives data-driven views from the planner's own logged sales (no market API).
/// "Selling well" = the user's own sale volume, mirroring the web Sales Insights.
/// </summary>
public static class PlannerInsights
{
    private const double DayMs = 864e5;

    public static bool IsSale(LogEntry e) => e.Source == "sale";

    /// <summary>Whether any lane item matches this name (case-insensitive, trimmed).</summary>
    public static bool IsOnPlan(PlannerData d, string name)
    {
        var key = name.Trim().ToLowerInvariant();
        foreach (var lane in Lanes.Order)
            if (d.Lanes.TryGetValue(lane, out var items))
                foreach (var it in items)
                    if (it.Name.Trim().ToLowerInvariant() == key) return true;
        return false;
    }

    /// <summary>The most recent sales, newest-first, capped at <paramref name="count"/>.</summary>
    public static List<LogEntry> RecentSales(PlannerData d, int count)
    {
        var sales = new List<LogEntry>();
        for (int i = d.Log.Count - 1; i >= 0 && sales.Count < count; i--)
            if (IsSale(d.Log[i])) sales.Add(d.Log[i]);
        return sales;
    }

    public sealed class Proposal
    {
        public string Name  = "";
        public int    Count;
        public long   Total;
        public long   Avg => Count > 0 ? Total / Count : 0;
    }

    /// <summary>
    /// Sold items (within the window) that are NOT on the plan, grouped by name and
    /// ranked by total gil — candidates to add to the plan.
    /// </summary>
    public static List<Proposal> Proposals(PlannerData d, int days)
    {
        var cutoff = PlannerStats.NowMs() - (long)(days * DayMs);
        var agg = new Dictionary<string, Proposal>();
        foreach (var e in d.Log)
        {
            if (!IsSale(e) || e.Ts < cutoff) continue;
            if (string.IsNullOrWhiteSpace(e.Note)) continue;
            if (IsOnPlan(d, e.Note)) continue;
            var key = e.Note.Trim().ToLowerInvariant();
            if (!agg.TryGetValue(key, out var p)) { p = new Proposal { Name = e.Note }; agg[key] = p; }
            p.Count += 1;
            p.Total += e.Amount;
        }
        return agg.Values.OrderByDescending(p => p.Total).ToList();
    }

    /// <summary>Map of plan-item name → # sales in the last <paramref name="days"/> days.</summary>
    public static Dictionary<string, int> RecentSaleCountByItem(PlannerData d, int days)
    {
        var cutoff = PlannerStats.NowMs() - (long)(days * DayMs);
        var map = new Dictionary<string, int>();
        foreach (var e in d.Log)
        {
            if (!IsSale(e) || e.Ts < cutoff || string.IsNullOrWhiteSpace(e.Note)) continue;
            var key = e.Note.Trim().ToLowerInvariant();
            map[key] = map.GetValueOrDefault(key, 0) + 1;
        }
        return map;
    }
}
