using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using QiqirnCompanion.Services;
using QiqirnCompanion.Windows;

namespace QiqirnCompanion;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/qiqirn";

    private readonly IDalamudPluginInterface _pi;
    private readonly ICommandManager         _commands;

    public readonly Configuration Config;
    private readonly ApiClient     _api;

    private readonly WindowSystem  _windowSystem  = new("QiqirnCompanion");
    private readonly MainWindow    _mainWindow;
    private readonly ConfigWindow  _configWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager         commandManager,
        IPlayerState            playerState)
    {
        _pi          = pluginInterface;
        _commands    = commandManager;

        // Load persisted config (or create default).
        Config = _pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(_pi);

        // Services
        _api = new ApiClient(Config.ApiBaseUrl);

        // Windows
        _mainWindow   = new MainWindow(Config, _api, playerState);
        _configWindow = new ConfigWindow(Config);
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);

        // Slash command
        _commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open/close the Qiqirn Companion window.",
        });

        // Hook into Dalamud's draw loop
        _pi.UiBuilder.Draw         += DrawUI;
        _pi.UiBuilder.OpenConfigUi += ToggleConfig;
        _pi.UiBuilder.OpenMainUi   += ToggleMain;
    }

    private void OnCommand(string command, string args) => ToggleMain();
    private void ToggleMain()   => _mainWindow.Toggle();
    private void ToggleConfig() => _configWindow.Toggle();
    private void DrawUI()       => _windowSystem.Draw();

    public void Dispose()
    {
        _pi.UiBuilder.Draw         -= DrawUI;
        _pi.UiBuilder.OpenConfigUi -= ToggleConfig;
        _pi.UiBuilder.OpenMainUi   -= ToggleMain;

        _commands.RemoveHandler(CommandName);
        _windowSystem.RemoveAllWindows();
        _api.Dispose();
    }
}
