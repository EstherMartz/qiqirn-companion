using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Hidden "easter egg" window for the guild's weekly Ruleta del Estilismo.
/// Reachable only via <c>/qiqirn ruleta</c>. The MC enters a name and the
/// person's haircut count, rolls a styling assignment, and copies a Spanish
/// summary to the clipboard to paste into chat. Nothing is auto-sent.
/// </summary>
public class RuletaWindow : Window, IDisposable
{
    private readonly Random _rng = new();

    private string      _name    = "";
    private int         _count   = 0;
    private RuletaRoll? _result  = null;
    private string      _message = "";

    public RuletaWindow() : base("Ruleta del Estilismo")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 200),
            MaximumSize = new Vector2(900, 400),
        };
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Vamo' a ponernos guapis.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        // Editing the name after a roll keeps the preview line in sync.
        if (ImGui.InputTextWithHint("##nombre", "Nombre", ref _name, 64) && _result != null)
            _message = RuletaRoll.Format(_name, _result.Value);

        ImGui.SetNextItemWidth(160);
        ImGui.InputInt("Cortes disponibles", ref _count);
        if (_count < 0) _count = 0;

        ImGui.Spacing();

        var canRoll = _count >= 1;
        if (!canRoll) ImGui.BeginDisabled();
        if (ImGui.Button("Daleee", new Vector2(120, 0)))
        {
            _result  = RuletaRoll.Roll(_count, _rng);
            _message = RuletaRoll.Format(_name, _result.Value);
        }
        if (!canRoll) ImGui.EndDisabled();

        if (_result == null) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped(_message);
        ImGui.Spacing();
        if (ImGui.Button("Copiar", new Vector2(120, 0)))
            ImGui.SetClipboardText(_message);
    }

    public void Dispose() { }
}
