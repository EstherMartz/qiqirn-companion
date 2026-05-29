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
    private readonly SearchWindow  _searchWindow;
    private readonly TradingWindow _tradingWindow;
    private readonly PlannerWindow _plannerWindow;
    private readonly CleanupWindow _cleanupWindow;
    private readonly SalesTracker  _salesTracker;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager         commandManager,
        IPlayerState            playerState,
        IChatGui                chatGui,
        IDataManager            dataManager)
    {
        _pi          = pluginInterface;
        _commands    = commandManager;

        // Load persisted config (or create default).
        Config = _pi.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(_pi);

        // Services
        _api = new ApiClient(Config.ApiBaseUrl);
        ItemInteractions.Initialize(chatGui);
        _salesTracker = new SalesTracker(chatGui, dataManager, Config);

        // Windows
        _searchWindow  = new SearchWindow(_api);
        _tradingWindow = new TradingWindow(_api);
        _plannerWindow = new PlannerWindow(Config, _salesTracker);
        _cleanupWindow = new CleanupWindow(_api);
        _mainWindow    = new MainWindow(Config, _api, playerState, _searchWindow, _tradingWindow, _plannerWindow, _cleanupWindow);
        _configWindow  = new ConfigWindow(Config);
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);
        _windowSystem.AddWindow(_searchWindow);
        _windowSystem.AddWindow(_tradingWindow);
        _windowSystem.AddWindow(_plannerWindow);
        _windowSystem.AddWindow(_cleanupWindow);

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
        _salesTracker.Dispose();
        _api.Dispose();
    }
}
