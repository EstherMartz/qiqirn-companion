using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using QiqirnCompanion.Services;
using System.Numerics;

namespace QiqirnCompanion.Windows;

/// <summary>
/// Unified settings UI rendered both as the main window's "Settings" tab and
/// inside the gear-icon Config window. Owns its edit buffers; the Save button
/// persists the text fields, while the hotkey and home-world auto-fill save
/// immediately when they change.
/// </summary>
public class SettingsPanel
{
    private readonly Configuration _config;
    private readonly HotkeyService _hotkey;
    private readonly IPlayerState  _playerState;

    private string _guildIdBuf;
    private string _apiBaseUrlBuf;
    private string _charOverrideBuf;
    private string _homeWorldBuf;

    public SettingsPanel(Configuration config, HotkeyService hotkey, IPlayerState playerState)
    {
        _config      = config;
        _hotkey      = hotkey;
        _playerState = playerState;
        _guildIdBuf      = config.GuildId;
        _apiBaseUrlBuf   = config.ApiBaseUrl;
        _charOverrideBuf = config.CharacterNameOverride;
        _homeWorldBuf    = config.HomeWorld;
    }

    private string BindingLabel()
    {
        if (_config.HotkeyKey == VirtualKey.NO_KEY) return "Unbound";
        var s = "";
        if (_config.HotkeyCtrl)  s += "Ctrl + ";
        if (_config.HotkeyAlt)   s += "Alt + ";
        if (_config.HotkeyShift) s += "Shift + ";
        return s + _config.HotkeyKey.GetFancyName();
    }

    private string? DetectedWorld()
    {
        // IPlayerState doesn't expose world directly; config allows overriding.
        // Return null until user sets it — DC-scope presets still work without it.
        return null;
    }

    public void DrawContent()
    {
        // ── Hotkey ────────────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(0.5f, 0.85f, 1f, 1), "Open hotkey");
        ImGui.TextUnformatted($"Current: {BindingLabel()}");

        if (_hotkey.IsCapturing)
        {
            ImGui.TextColored(new Vector4(1, 0.9f, 0.4f, 1), "Press a key…  (Esc to cancel)");
            if (ImGui.Button("Cancel##hk")) _hotkey.CancelCapture();
        }
        else
        {
            if (ImGui.Button("Set hotkey")) _hotkey.BeginCapture();
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                _config.HotkeyKey  = VirtualKey.NO_KEY;
                _config.HotkeyCtrl = _config.HotkeyAlt = _config.HotkeyShift = false;
                _config.Save();
            }
        }
        ImGui.TextDisabled("Toggles the Qiqirn window. A modifier (Ctrl/Alt/Shift) is recommended.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Connection ────────────────────────────────────────────────────────
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Guild ID##gid", ref _guildIdBuf, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Your Discord server (guild) ID.\nRight-click server icon in Discord → Copy Server ID.");

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("API URL##url", ref _apiBaseUrlBuf, 128);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leave as https://qiqirn.tools unless you are self-hosting.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ── Game-derived (character + home world) ──────────────────────────────
        var detectedName  = _playerState.CharacterName;
        var detectedWorld = DetectedWorld();

        ImGui.TextUnformatted($"Detected character: {detectedName ?? "—"}");
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Character override##char", ref _charOverrideBuf, 64);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leave blank to use the detected character name.");

        ImGui.Spacing();

        ImGui.TextUnformatted($"Detected home world: {detectedWorld ?? "—"}");
        // Auto-fill home world from the game when the user hasn't set one.
        if (string.IsNullOrWhiteSpace(_homeWorldBuf) && detectedWorld != null)
        {
            _homeWorldBuf = detectedWorld;
            if (_config.HomeWorld != detectedWorld)
            {
                _config.HomeWorld = detectedWorld;
                _config.Save();
            }
        }
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Home World override##world", ref _homeWorldBuf, 32);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Auto-filled from the game. Override for logged-out / DC-travel use.\nRequired for home-scope trading presets.");

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
            _config.HomeWorld             = _homeWorldBuf.Trim();
            _config.Save();
        }
    }
}
