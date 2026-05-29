using System;
using System.Collections.Generic;
using System.Globalization;

namespace QiqirnCompanion.Planner;

/// <summary>Pure stat helpers ported from the web's plannerStats.ts.</summary>
public static class PlannerStats
{
    private const double DayMs = 864e5;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Local calendar date as yyyy-MM-dd (matches the web's per-user day).</summary>
    public static string TodayStr(long? nowMs = null)
    {
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(nowMs ?? NowMs()).LocalDateTime;
        return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string DayOf(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static long TodaySum(IEnumerable<LogEntry> log, long? nowMs = null)
    {
        var today = TodayStr(nowMs);
        long sum = 0;
        foreach (var l in log) if (DayOf(l.Ts) == today) sum += l.Amount;
        return sum;
    }

    public static long WeekSum(IEnumerable<LogEntry> log, long? nowMs = null)
    {
        var now = nowMs ?? NowMs();
        long sum = 0;
        foreach (var l in log) if (now - l.Ts < 7 * DayMs) sum += l.Amount;
        return sum;
    }

    public static int ElapsedDays(long startTs, long? nowMs = null)
    {
        var now = nowMs ?? NowMs();
        return Math.Max(1, (int)Math.Ceiling((now - startTs) / DayMs));
    }

    public static double Rate(double week, int days) => week > 0 ? week / Math.Min(7, days) : 0;

    public static int? Eta(double remaining, double dailyRate) =>
        dailyRate > 0 ? (int)Math.Ceiling(remaining / dailyRate) : null;

    public static double Pct(double current, double target)
    {
        if (target <= 0) return 0;
        return Math.Min(100, current / target * 100);
    }

    /// <summary>Abbreviate gil: 1.2B / 3.4M / 56K / 789.</summary>
    public static string Abbr(double n)
    {
        var abs = Math.Max(0, n);
        if (abs >= 1e9) return Trim((abs / 1e9).ToString("0.00", CultureInfo.InvariantCulture)) + "B";
        if (abs >= 1e6) return Trim((abs / 1e6).ToString("0.0", CultureInfo.InvariantCulture)) + "M";
        if (abs >= 1e3) return (abs / 1e3).ToString("0", CultureInfo.InvariantCulture) + "K";
        return Math.Round(abs).ToString(CultureInfo.InvariantCulture);
    }

    private static string Trim(string s) =>
        s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;

    public static string SupClass(double? supply)
    {
        if (supply == null) return "";
        if (supply < 2) return "low";
        if (supply <= 7) return "mid";
        return "high";
    }
}
