using System.Collections.Generic;

namespace QiqirnCompanion.Planner;

// Lane keys mirror the web: craft / gather / content / passive.
public static class Lanes
{
    public const string Craft   = "craft";
    public const string Gather  = "gather";
    public const string Content = "content";
    public const string Passive = "passive";

    public static readonly string[] Order = { Craft, Gather, Content, Passive };

    public static (string Name, string Desc) Meta(string lane) => lane switch
    {
        Craft   => ("Craft",         "your engine"),
        Gather  => ("Gather & Sell", "near-pure profit"),
        Content => ("Content Farm",  "high-ticket lottery"),
        Passive => ("Passive",       "set & forget"),
        _       => (lane, ""),
    };
}

/// <summary>A single planned item within a lane.</summary>
public class PlanItem
{
    public string  Id      { get; set; } = "";
    public string  Name    { get; set; } = "";
    public string  Src     { get; set; } = "";
    public long    Price   { get; set; }          // unit sale price
    public long    Cost    { get; set; }          // input cost per unit
    public double  PerDay  { get; set; }          // estimated units sold / day
    public double? Supply  { get; set; }          // days of inventory (null = unknown)
    public bool    Active  { get; set; } = true;
    public long    Earned  { get; set; }          // cumulative gil earned
    public int     Units   { get; set; }          // cumulative units sold
}

/// <summary>A treasury ledger entry (a sale or manual gil log).</summary>
public class LogEntry
{
    public long    Ts     { get; set; }           // unix ms
    public long    Amount { get; set; }
    public string  Note   { get; set; } = "";
    public string? ItemId { get; set; }
}

public class Goal
{
    public long Current { get; set; }
    public long Target  { get; set; }
    public long StartTs { get; set; }             // unix ms
}

public class DailyState
{
    public string                   Date { get; set; } = "";    // yyyy-MM-dd
    public Dictionary<string, bool> Done { get; set; } = new();
}

public class DailyTask
{
    public string Id    { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>The entire planner state — persisted in the plugin Configuration.</summary>
public class PlannerData
{
    public int                              Version = 1;
    public Goal                             Goal    { get; set; } = new();
    public Dictionary<string, List<PlanItem>> Lanes { get; set; } = new();
    public List<LogEntry>                   Log     { get; set; } = new();
    public DailyState                       Daily   { get; set; } = new();
}
