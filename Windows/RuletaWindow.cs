using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;
using System.Reflection;

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

    // Background image, embedded in the DLL (see QiqirnCompanion.csproj). The
    // shared texture is owned by Dalamud's texture system — never disposed here.
    private readonly ISharedImmediateTexture _bg;

    private string      _name    = "";
    private int         _count   = 0;
    private RuletaRoll? _result  = null;
    private string      _message = "";

    public RuletaWindow(ITextureProvider textures) : base("Ruleta del Estilismo")
    {
        _bg = textures.GetFromManifestResource(Assembly.GetExecutingAssembly(), "QiqirnCompanion.bg.jpeg");
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 200),
            MaximumSize = new Vector2(900, 400),
        };
    }

    public override void Draw()
    {
        DrawBackground();

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

    /// <summary>
    /// Paint the embedded image across the window's client area (below the title
    /// bar), cover-fitted to preserve aspect, then a translucent black scrim so
    /// the foreground text stays readable. Drawn first so every widget that
    /// follows renders on top of it in the same window draw list.
    /// </summary>
    private void DrawBackground()
    {
        var wrap = _bg.GetWrapOrEmpty();
        if (wrap.Handle == 0) return; // not yet loaded this frame

        var dl   = ImGui.GetWindowDrawList();
        var pMin = ImGui.GetWindowPos() + new Vector2(0, ImGui.GetFrameHeight());
        var pMax = ImGui.GetWindowPos() + ImGui.GetWindowSize();

        var box = pMax - pMin;
        if (box.X <= 0 || box.Y <= 0) return;

        // Cover-fit: scale the image to fill the box, cropping the overflowing
        // axis symmetrically via UVs (source may be any aspect; bg.jpeg is square).
        var img        = wrap.Size;
        var boxAspect  = box.X / box.Y;
        var imgAspect  = img.X / img.Y;
        Vector2 uv0, uv1;
        if (boxAspect > imgAspect)
        {
            var visible = imgAspect / boxAspect;       // fraction of height shown
            uv0 = new Vector2(0, (1 - visible) / 2);
            uv1 = new Vector2(1, 1 - (1 - visible) / 2);
        }
        else
        {
            var visible = boxAspect / imgAspect;        // fraction of width shown
            uv0 = new Vector2((1 - visible) / 2, 0);
            uv1 = new Vector2(1 - (1 - visible) / 2, 1);
        }

        dl.AddImage(wrap.Handle, pMin, pMax, uv0, uv1);
        dl.AddRectFilled(pMin, pMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.55f)));
    }

    public void Dispose() { }
}
