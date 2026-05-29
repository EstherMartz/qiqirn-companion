using System;
using System.Collections.Generic;

namespace QiqirnCompanion.Planner;

/// <summary>
/// State mutations ported from the web's plannerStore.ts. These mutate the
/// PlannerData in place; the caller persists afterwards via Config.Save().
/// </summary>
public static class PlannerLogic
{
    public static string NewItemId() => "i" + Guid.NewGuid().ToString("N").Substring(0, 6);

    private static List<PlanItem> Lane(PlannerData d, string lane)
    {
        if (!d.Lanes.TryGetValue(lane, out var list))
        {
            list = new List<PlanItem>();
            d.Lanes[lane] = list;
        }
        return list;
    }

    private static PlanItem? Find(PlannerData d, string lane, string itemId)
        => Lane(d, lane).Find(x => x.Id == itemId);

    /// <summary>Log gil to the treasury, optionally tagged to a plan item.</summary>
    public static void LogGil(PlannerData d, long amount, string? itemId = null, string? note = null, string source = "manual")
    {
        if (amount == 0) return;
        var noteText = note ?? "Manual entry";
        if (!string.IsNullOrEmpty(itemId))
        {
            foreach (var lane in Lanes.Order)
            {
                var it = Find(d, lane, itemId!);
                if (it != null) { it.Earned += amount; it.Units += 1; noteText = it.Name; break; }
            }
        }
        d.Goal.Current = Math.Max(0, d.Goal.Current + amount);
        d.Log.Add(new LogEntry { Ts = PlannerStats.NowMs(), Amount = amount, Note = noteText, ItemId = itemId, Source = source });
    }

    /// <summary>Record one sale of a lane item at its unit price.</summary>
    public static void RecordSale(PlannerData d, string lane, string itemId)
    {
        var it = Find(d, lane, itemId);
        if (it == null) return;
        it.Units += 1;
        it.Earned += it.Price;
        d.Goal.Current += it.Price;
        d.Log.Add(new LogEntry { Ts = PlannerStats.NowMs(), Amount = it.Price, Note = it.Name, ItemId = itemId, Source = "sale" });
    }

    /// <summary>Undo the most recent recorded sale for an item.</summary>
    public static void ReverseSale(PlannerData d, string lane, string itemId)
    {
        var it = Find(d, lane, itemId);
        if (it == null || it.Units <= 0) return;
        int idx = -1;
        for (int i = d.Log.Count - 1; i >= 0; i--)
        {
            if (d.Log[i].ItemId == itemId && d.Log[i].Amount > 0) { idx = i; break; }
        }
        long amount = idx >= 0 ? d.Log[idx].Amount : it.Price;
        it.Units -= 1;
        it.Earned = Math.Max(0, it.Earned - amount);
        if (idx >= 0) d.Log.RemoveAt(idx);
        d.Goal.Current = Math.Max(0, d.Goal.Current - amount);
    }

    public static void AddItem(PlannerData d, string lane, PlanItem partial)
    {
        partial.Id = NewItemId();
        partial.Active = true;
        partial.Earned = 0;
        partial.Units = 0;
        Lane(d, lane).Add(partial);
    }

    public static void RemoveItem(PlannerData d, string lane, string itemId)
        => Lane(d, lane).RemoveAll(x => x.Id == itemId);

    public static void ToggleActive(PlannerData d, string lane, string itemId)
    {
        var it = Find(d, lane, itemId);
        if (it != null) it.Active = !it.Active;
    }

    public static void SetGoal(PlannerData d, long? current = null, long? target = null)
    {
        if (current != null) d.Goal.Current = Math.Max(0, current.Value);
        if (target != null)  d.Goal.Target  = Math.Max(1, target.Value);
    }

    public static void ToggleDaily(PlannerData d, string taskId)
    {
        if (d.Daily.Done.TryGetValue(taskId, out var on) && on) d.Daily.Done.Remove(taskId);
        else d.Daily.Done[taskId] = true;
    }

    /// <summary>Clear the daily checklist when the calendar day has changed.</summary>
    public static void DailyResetIfStale(PlannerData d)
    {
        var today = PlannerStats.TodayStr();
        if (d.Daily.Date == today) return;
        d.Daily.Date = today;
        d.Daily.Done = new Dictionary<string, bool>();
    }

    public static void DeleteLogEntry(PlannerData d, long ts)
    {
        var entry = d.Log.Find(l => l.Ts == ts);
        if (entry == null) return;
        d.Log.RemoveAll(l => l.Ts == ts);
        d.Goal.Current = Math.Max(0, d.Goal.Current - entry.Amount);
    }
}
