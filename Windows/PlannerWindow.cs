using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using QiqirnCompanion.Planner;
using QiqirnCompanion.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace QiqirnCompanion.Windows;

public class PlannerWindow : Window, IDisposable
{
    private readonly Configuration _config;

    // Goal-edit state
    private bool   _editingGoal;
    private string _goalCurrentBuf = "";
    private string _goalTargetBuf  = "";

    // Quick log
    private string _logAmountBuf = "";

    // Per-lane add-item buffers
    private sealed class AddBuffer
    {
        public string Name = "", Src = "", Price = "", Cost = "", PerDay = "", Supply = "";
    }
    private readonly Dictionary<string, AddBuffer> _add = new();
    private readonly HashSet<string> _showAdd = new();

    public PlannerWindow(Configuration config) : base("Gil Planner##planner")
    {
        _config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 400),
            MaximumSize = new Vector2(2200, 1400),
        };
    }

    private PlannerData Data
    {
        get
        {
            if (_config.Planner == null)
            {
                _config.Planner = SeedPlanner.CreateDefault();
                _config.Save();
            }
            return _config.Planner;
        }
    }

    private void Save() => _config.Save();

    public override void Draw() => DrawContent();

    public void DrawContent()
    {
        var d = Data;
        PlannerLogic.DailyResetIfStale(d);

        DrawHero(d);
        ImGui.Separator();
        DrawLanes(d);
        ImGui.Separator();
        DrawDailyRhythm(d);
        ImGui.Separator();
        DrawLedger(d);
    }

    // ── Hero / goal ─────────────────────────────────────────────────────────

    private void DrawHero(PlannerData d)
    {
        var week = PlannerStats.WeekSum(d.Log);
        var days = PlannerStats.ElapsedDays(d.Goal.StartTs);
        var rate = PlannerStats.Rate(week, days);
        var remaining = Math.Max(0, d.Goal.Target - d.Goal.Current);
        var etaDays = PlannerStats.Eta(remaining, rate);
        var today = PlannerStats.TodaySum(d.Log);
        var netProfit = NetProfit(d);

        ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f),
            $"{PlannerStats.Abbr(d.Goal.Current)}  /  {PlannerStats.Abbr(d.Goal.Target)} gil");

        var frac = (float)(PlannerStats.Pct(d.Goal.Current, d.Goal.Target) / 100.0);
        ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{PlannerStats.Pct(d.Goal.Current, d.Goal.Target):F1}%");

        // Stat line
        ImGui.TextDisabled(
            $"Today: {PlannerStats.Abbr(today)}   " +
            $"7-day: {PlannerStats.Abbr(week)}   " +
            $"Rate: {PlannerStats.Abbr(rate)}/day   " +
            $"ETA: {(etaDays.HasValue ? etaDays.Value + " days" : "—")}   " +
            $"Net profit: {PlannerStats.Abbr(netProfit)}");

        // Actions row
        if (ImGui.Button("Use game gil"))
        {
            var gil = GilReader.CurrentGil();
            if (gil.HasValue) { PlannerLogic.SetGoal(d, current: gil.Value); Save(); }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Set current treasury to your live in-game gil.");

        ImGui.SameLine();
        if (ImGui.Button(_editingGoal ? "Close goal edit" : "Edit goal"))
        {
            _editingGoal = !_editingGoal;
            if (_editingGoal)
            {
                _goalCurrentBuf = d.Goal.Current.ToString(CultureInfo.InvariantCulture);
                _goalTargetBuf  = d.Goal.Target.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (_editingGoal)
        {
            ImGui.SetNextItemWidth(140);
            ImGui.InputText("Current##goalcur", ref _goalCurrentBuf, 20);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            ImGui.InputText("Target##goaltgt", ref _goalTargetBuf, 20);
            ImGui.SameLine();
            if (ImGui.Button("Apply##goal"))
            {
                long? cur = long.TryParse(_goalCurrentBuf, out var c) ? c : null;
                long? tgt = long.TryParse(_goalTargetBuf, out var t) ? t : null;
                PlannerLogic.SetGoal(d, cur, tgt);
                Save();
                _editingGoal = false;
            }
        }

        // Quick log gil
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##logamt", "amount (+/-)", ref _logAmountBuf, 20);
        ImGui.SameLine();
        if (ImGui.Button("Log gil") && long.TryParse(_logAmountBuf, out var amt) && amt != 0)
        {
            PlannerLogic.LogGil(d, amt);
            _logAmountBuf = "";
            Save();
        }
    }

    private static long NetProfit(PlannerData d)
    {
        long net = 0;
        foreach (var lane in Lanes.Order)
            if (d.Lanes.TryGetValue(lane, out var items))
                foreach (var it in items)
                    net += it.Earned - it.Cost * it.Units;
        return net;
    }

    // ── Lanes ─────────────────────────────────────────────────────────────

    private void DrawLanes(PlannerData d)
    {
        foreach (var lane in Lanes.Order)
        {
            var (nm, desc) = Lanes.Meta(lane);
            if (!d.Lanes.TryGetValue(lane, out var items)) { items = new(); d.Lanes[lane] = items; }

            if (!ImGui.CollapsingHeader($"{nm}  ({items.Count})##lane{lane}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            ImGui.TextDisabled(desc);
            DrawLaneTable(d, lane, items);

            // Add-item toggle + form
            if (ImGui.SmallButton($"+ add item##add{lane}"))
            {
                if (!_showAdd.Add(lane)) _showAdd.Remove(lane);
                if (!_add.ContainsKey(lane)) _add[lane] = new AddBuffer();
            }
            if (_showAdd.Contains(lane))
                DrawAddForm(d, lane);

            ImGui.Spacing();
        }
    }

    private void DrawLaneTable(PlannerData d, string lane, List<PlanItem> items)
    {
        if (items.Count == 0) { ImGui.TextDisabled("  (no items)"); return; }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable($"##lanetbl{lane}", 8, flags)) return;

        ImGui.TableSetupColumn("On",     ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableSetupColumn("Item",   ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Price",  ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("/day",   ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Supply", ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Sold",   ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Earned", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("",       ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        string? removeId = null;
        foreach (var it in items)
        {
            ImGui.TableNextRow();
            ImGui.PushID(it.Id);

            ImGui.TableSetColumnIndex(0);
            var active = it.Active;
            if (ImGui.Checkbox("##on", ref active)) { PlannerLogic.ToggleActive(d, lane, it.Id); Save(); }

            ImGui.TableSetColumnIndex(1);
            if (it.Active) ImGui.TextUnformatted(it.Name);
            else ImGui.TextDisabled(it.Name);
            if (!string.IsNullOrEmpty(it.Src)) ImGui.TextDisabled($"  {it.Src}");

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(PlannerStats.Abbr(it.Price));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(it.PerDay.ToString("0.#", CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(4);
            DrawSupply(it.Supply);

            ImGui.TableSetColumnIndex(5);
            ImGui.TextUnformatted(it.Units.ToString());

            ImGui.TableSetColumnIndex(6);
            ImGui.TextUnformatted(PlannerStats.Abbr(it.Earned));

            ImGui.TableSetColumnIndex(7);
            if (ImGui.SmallButton("+")) { PlannerLogic.RecordSale(d, lane, it.Id); Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Record a sale (+{PlannerStats.Abbr(it.Price)} gil)");
            ImGui.SameLine();
            if (ImGui.SmallButton("-")) { PlannerLogic.ReverseSale(d, lane, it.Id); Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Undo last sale");
            ImGui.SameLine();
            if (ImGui.SmallButton("x")) removeId = it.Id;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove item");

            ImGui.PopID();
        }

        ImGui.EndTable();

        if (removeId != null) { PlannerLogic.RemoveItem(d, lane, removeId); Save(); }
    }

    private static void DrawSupply(double? supply)
    {
        if (supply == null) { ImGui.TextDisabled("—"); return; }
        var cls = PlannerStats.SupClass(supply);
        var color = cls switch
        {
            "low"  => new Vector4(0.4f, 0.9f, 0.4f, 1f),   // low supply = good (sells fast)
            "mid"  => new Vector4(1f, 0.85f, 0.3f, 1f),
            "high" => new Vector4(1f, 0.5f, 0.3f, 1f),
            _      => new Vector4(1f, 1f, 1f, 1f),
        };
        ImGui.TextColored(color, supply.Value.ToString("0.#", CultureInfo.InvariantCulture) + "d");
    }

    private void DrawAddForm(PlannerData d, string lane)
    {
        var b = _add[lane];
        ImGui.Indent();
        ImGui.SetNextItemWidth(180); ImGui.InputTextWithHint($"##n{lane}", "name", ref b.Name, 128);
        ImGui.SameLine(); ImGui.SetNextItemWidth(140); ImGui.InputTextWithHint($"##s{lane}", "source", ref b.Src, 128);
        ImGui.SetNextItemWidth(90);  ImGui.InputTextWithHint($"##p{lane}", "price", ref b.Price, 16);
        ImGui.SameLine(); ImGui.SetNextItemWidth(90);  ImGui.InputTextWithHint($"##c{lane}", "cost", ref b.Cost, 16);
        ImGui.SameLine(); ImGui.SetNextItemWidth(70);  ImGui.InputTextWithHint($"##d{lane}", "/day", ref b.PerDay, 10);
        ImGui.SameLine(); ImGui.SetNextItemWidth(70);  ImGui.InputTextWithHint($"##u{lane}", "supply", ref b.Supply, 10);
        ImGui.SameLine();
        if (ImGui.Button($"Add##commit{lane}") && !string.IsNullOrWhiteSpace(b.Name))
        {
            var item = new PlanItem
            {
                Name   = b.Name.Trim(),
                Src    = b.Src.Trim(),
                Price  = long.TryParse(b.Price, out var pr) ? pr : 0,
                Cost   = long.TryParse(b.Cost, out var co) ? co : 0,
                PerDay = double.TryParse(b.PerDay, NumberStyles.Any, CultureInfo.InvariantCulture, out var pd) ? pd : 0,
                Supply = double.TryParse(b.Supply, NumberStyles.Any, CultureInfo.InvariantCulture, out var su) ? su : (double?)null,
            };
            PlannerLogic.AddItem(d, lane, item);
            Save();
            _add[lane] = new AddBuffer();
            _showAdd.Remove(lane);
        }
        ImGui.Unindent();
    }

    // ── Daily rhythm ───────────────────────────────────────────────────────

    private void DrawDailyRhythm(PlannerData d)
    {
        var doneCount = SeedPlanner.DailyTasks.Count(t => d.Daily.Done.TryGetValue(t.Id, out var v) && v);
        if (!ImGui.CollapsingHeader($"Daily Rhythm  ({doneCount}/{SeedPlanner.DailyTasks.Length} today)##daily",
                ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var task in SeedPlanner.DailyTasks)
        {
            var done = d.Daily.Done.TryGetValue(task.Id, out var v) && v;
            if (ImGui.Checkbox($"{task.Label}##{task.Id}", ref done))
            {
                PlannerLogic.ToggleDaily(d, task.Id);
                Save();
            }
        }
    }

    // ── Ledger ─────────────────────────────────────────────────────────────

    private void DrawLedger(PlannerData d)
    {
        if (!ImGui.CollapsingHeader($"Ledger  ({d.Log.Count})##ledger")) return;

        if (d.Log.Count == 0) { ImGui.TextDisabled("No entries yet."); return; }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("##ledger", 4, flags, new Vector2(0, 200))) return;

        ImGui.TableSetupColumn("When",   ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn("Note",   ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("",       ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableHeadersRow();

        long deleteTs = 0;
        // Newest first; cap rendered rows for performance.
        foreach (var e in d.Log.AsEnumerable().Reverse().Take(100))
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(e.Ts).LocalDateTime;
            ImGui.TextDisabled(when.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture));

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(e.Note);

            ImGui.TableSetColumnIndex(2);
            var color = e.Amount >= 0 ? new Vector4(0.4f, 0.9f, 0.4f, 1f) : new Vector4(1f, 0.5f, 0.3f, 1f);
            ImGui.TextColored(color, (e.Amount >= 0 ? "+" : "") + PlannerStats.Abbr(e.Amount));

            ImGui.TableSetColumnIndex(3);
            ImGui.PushID((int)(e.Ts & 0x7fffffff));
            if (ImGui.SmallButton("x")) deleteTs = e.Ts;
            ImGui.PopID();
        }

        ImGui.EndTable();

        if (deleteTs != 0) { PlannerLogic.DeleteLogEntry(d, deleteTs); Save(); }
    }

    public void Dispose() { }
}
