using System;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Pure roll + formatting logic for the Ruleta del Estilismo feature. No ImGui
/// here — kept isolated so the randomization and message format stay trivially
/// correct and independent of the window's draw loop.
/// </summary>
public readonly record struct RuletaRoll(
    int Cut, int Count, int BaseRow, int BaseCol, int HiRow, int HiCol)
{
    /// <summary>Hair-color grid dimensions from the in-game appearance menu.</summary>
    public const int Columns = 8;
    public const int Rows    = 24;

    /// <summary>
    /// Roll a full styling assignment. <paramref name="count"/> is the person's
    /// total available haircuts; the caller guarantees it is &gt;= 1.
    /// </summary>
    public static RuletaRoll Roll(int count, Random rng) => new(
        Cut:     rng.Next(1, count + 1),
        Count:   count,
        BaseRow: rng.Next(1, Rows + 1),
        BaseCol: rng.Next(1, Columns + 1),
        HiRow:   rng.Next(1, Rows + 1),
        HiCol:   rng.Next(1, Columns + 1));

    /// <summary>
    /// Build the paste-ready Spanish message. ASCII only — FFXIV chat filters
    /// some Unicode, and none of these words need accents.
    /// </summary>
    public static string Format(string name, RuletaRoll r) =>
        $"Ruleta del Estilismo - {name} | Corte: {r.Cut}/{r.Count} | " +
        $"Color base: F{r.BaseRow} C{r.BaseCol} | Mechas: F{r.HiRow} C{r.HiCol}";
}
