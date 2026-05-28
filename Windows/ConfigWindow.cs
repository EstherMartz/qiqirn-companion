using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace QiqirnCompanion.Windows;

public class ConfigWindow : Window
{
    private readonly Configuration _config;

    // Edit buffers — ImGui InputText operates on string refs in .NET bindings.
    private string _guildIdBuf;
    private string _apiBaseUrlBuf;
    private string _charOverrideBuf;

    public ConfigWindow(Configuration config)
        : base("Qiqirn Companion — Config##config",
               ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar)
    {
        _config = config;
        Size    = new Vector2(440, 220);
        SizeCondition = ImGuiCond.Always;

        _guildIdBuf      = config.GuildId;
        _apiBaseUrlBuf   = config.ApiBaseUrl;
        _charOverrideBuf = config.CharacterNameOverride;
    }

    public override void OnOpen()
    {
        // Refresh buffers each time the window is opened so edits start fresh.
        _guildIdBuf      = _config.GuildId;
        _apiBaseUrlBuf   = _config.ApiBaseUrl;
        _charOverrideBuf = _config.CharacterNameOverride;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Configure QiqirnCompanion. Hover any label for help.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Guild ID##gid", ref _guildIdBuf, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your Discord server (guild) ID.\nRight-click server icon in Discord → Copy Server ID.");

        ImGui.Spacing();

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("API URL##url", ref _apiBaseUrlBuf, 128);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leave as https://qiqirn.tools unless you are self-hosting.");

        ImGui.Spacing();

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Character Name Override##char", ref _charOverrideBuf, 64);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leave empty to use your logged-in character name automatically.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Save"))
        {
            _config.GuildId               = _guildIdBuf.Trim();
            _config.ApiBaseUrl            = string.IsNullOrWhiteSpace(_apiBaseUrlBuf)
                                              ? "https://qiqirn.tools"
                                              : _apiBaseUrlBuf.Trim();
            _config.CharacterNameOverride = _charOverrideBuf.Trim();
            _config.Save();
            IsOpen = false;
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
            IsOpen = false;
    }
}
