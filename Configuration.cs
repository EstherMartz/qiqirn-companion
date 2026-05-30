using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using QiqirnCompanion.Planner;
using System;

namespace QiqirnCompanion;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Discord Guild (server) ID for your FC. Paste once from Discord.</summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>Base URL for the qiqirn.tools API. Change only if self-hosting.</summary>
    public string ApiBaseUrl { get; set; } = "https://qiqirn.tools";

    /// <summary>
    /// Leave empty to auto-read from the game client (ClientState.LocalPlayer.Name).
    /// Fill in only if you want to use a different name (e.g., for an alt).
    /// </summary>
    public string CharacterNameOverride { get; set; } = string.Empty;

    /// <summary>
    /// Home world name (e.g. "Phantom"). Required for home-scope trading queries.
    /// </summary>
    public string HomeWorld { get; set; } = string.Empty;

    /// <summary>Standalone gil planner state (seeded on first use).</summary>
    public PlannerData? Planner { get; set; }

    /// <summary>Auto-log retainer market sales into the planner from chat.</summary>
    public bool AutoLogRetainerSales { get; set; } = true;

    /// <summary>Hotkey that toggles the main window. NO_KEY = unbound.</summary>
    public VirtualKey HotkeyKey   { get; set; } = VirtualKey.NO_KEY;
    public bool       HotkeyCtrl  { get; set; }
    public bool       HotkeyAlt   { get; set; }
    public bool       HotkeyShift { get; set; }

    // Injected by Plugin.cs after loading — used to save.
    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    public void Save()
    {
        _pluginInterface!.SavePluginConfig(this);
    }
}
