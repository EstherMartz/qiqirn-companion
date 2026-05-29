using System.Collections.Generic;

namespace QiqirnCompanion.Planner;

/// <summary>Default starter plan, mirroring the web's seedPlanner.ts.</summary>
public static class SeedPlanner
{
    public static readonly DailyTask[] DailyTasks =
    {
        new() { Id = "d1", Label = "Collect retainer sales & re-list (small undercut)" },
        new() { Id = "d2", Label = "Craft 2-3 lowest-supply items" },
        new() { Id = "d3", Label = "One gathering pass (high-velocity mats)" },
        new() { Id = "d4", Label = "One content run (Occult Crescent / Cosmic)" },
        new() { Id = "d5", Label = "Check spiritbond fodder" },
        new() { Id = "d6", Label = "Refresh qiqirn.tools market data" },
    };

    private static PlanItem Mk(string name, string src, long price, long cost, double perDay, double? supply) => new()
    {
        Id = PlannerLogic.NewItemId(),
        Name = name, Src = src, Price = price, Cost = cost,
        PerDay = perDay, Supply = supply, Active = true, Earned = 0, Units = 0,
    };

    public static PlannerData CreateDefault()
    {
        return new PlannerData
        {
            Goal = new Goal { Current = 10_000_000, Target = 100_000_000, StartTs = PlannerStats.NowMs() },
            Log = new List<LogEntry>(),
            Daily = new DailyState(),
            Lanes = new Dictionary<string, List<PlanItem>>
            {
                [Lanes.Craft] = new()
                {
                    Mk("Plain Hooded Tunic", "Weaver", 4_025_000, 1_200_000, 1.1, 0.9),
                    Mk("Crested Shirt of Crafting", "iL750 gear", 399_500, 120_000, 1.7, 1.8),
                    Mk("Courtly Lover's Partisan", "weapon craft", 437_961, 150_000, 1.6, 1.9),
                    Mk("Courtly Lover's Cane", "weapon craft", 579_896, 180_000, 1.0, 1.0),
                    Mk("Grade 4 Gemdraughts (filler)", "Alchemist · vol", 4_150, 1_500, 250, 2.0),
                },
                [Lanes.Gather] = new()
                {
                    Mk("Yollal Extract", "gatherable", 7_900, 0, 154, null),
                    Mk("Everkeep Resin", "gatherable", 7_999, 0, 108, null),
                    Mk("Levinchrome Aethersand", "Cosmic Auxesia", 1_800, 0, 266, null),
                    Mk("Double Duracoat", "gatherable", 7_499, 0, 69, null),
                },
                [Lanes.Content] = new()
                {
                    Mk("Occult Bracelet of Blood", "Occult Crescent", 40_000_000, 0, 0.3, null),
                    Mk("Occult Necklace of Blood", "Occult Crescent", 43_249_499, 0, 0.1, null),
                    Mk("Cosmoboard", "Cosmic Exploration", 2_964_474, 0, 0.3, null),
                },
                [Lanes.Passive] = new()
                {
                    Mk("Craftsman's Command Materia XII", "spiritbond", 14_392, 0, 20, null),
                    Mk("Gatherer's Guile Materia XII", "spiritbond", 6_352, 0, 34, null),
                    Mk("Timeworn Gargantuaskin Map", "gather + sell", 41_499, 0, 2.9, null),
                },
            },
        };
    }
}
