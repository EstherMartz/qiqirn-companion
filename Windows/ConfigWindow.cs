using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace QiqirnCompanion.Windows;

public class ConfigWindow : Window
{
    private readonly SettingsPanel _settings;

    public ConfigWindow(SettingsPanel settings)
        : base("Qiqirn Companion — Config##config", ImGuiWindowFlags.NoScrollbar)
    {
        _settings = settings;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 360),
            MaximumSize = new Vector2(600, 800),
        };
    }

    public override void Draw() => _settings.DrawContent();
}
