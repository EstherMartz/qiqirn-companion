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
    private readonly ItemInfoWindow _itemInfoWindow;
    private readonly TradingWindow _tradingWindow;
    private readonly PlannerWindow _plannerWindow;
    private readonly CleanupWindow _cleanupWindow;
    private readonly SalesTracker  _salesTracker;
    private readonly ContextMenuService _contextMenuService;
    private readonly HotkeyService  _hotkeyService;
    private readonly SettingsPanel  _settingsPanel;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager         commandManager,
        IPlayerState            playerState,
        IChatGui                chatGui,
        IDataManager            dataManager,
        IContextMenu            contextMenu,
        IFramework              framework,
        IKeyState               keyState)
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

        // Hotkey service + unified settings panel (constructed before the windows
        // that consume the panel). The toggle lambda reads _mainWindow lazily.
        _hotkeyService = new HotkeyService(framework, keyState, Config, () => _mainWindow.Toggle());
        _settingsPanel = new SettingsPanel(Config, _hotkeyService, playerState);

        // Windows
        _itemInfoWindow = new ItemInfoWindow(_api);
        _searchWindow  = new SearchWindow(_api, _itemInfoWindow);
        _tradingWindow = new TradingWindow(_api);
        _plannerWindow = new PlannerWindow(Config, _salesTracker);
        _cleanupWindow = new CleanupWindow(_api);
        _mainWindow    = new MainWindow(Config, _api, playerState, _searchWindow, _tradingWindow, _plannerWindow, _cleanupWindow, _settingsPanel);
        _configWindow  = new ConfigWindow(_settingsPanel);
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);
        _windowSystem.AddWindow(_searchWindow);
        _windowSystem.AddWindow(_itemInfoWindow);
        _windowSystem.AddWindow(_tradingWindow);
        _windowSystem.AddWindow(_plannerWindow);
        _windowSystem.AddWindow(_cleanupWindow);

        // Native game right-click menu → open item info.
        _contextMenuService = new ContextMenuService(contextMenu, dataManager, _itemInfoWindow.Show);

        // Slash command
        _commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/qiqirn — open the window.  /qiqirn <item> — search for an item.",
        });

        // Hook into Dalamud's draw loop
        _pi.UiBuilder.Draw         += DrawUI;
        _pi.UiBuilder.OpenConfigUi += ToggleConfig;
        _pi.UiBuilder.OpenMainUi   += ToggleMain;
    }

    private void OnCommand(string command, string args)
    {
        var query = args.Trim();
        if (query.Length == 0)
        {
            ToggleMain();
            return;
        }

        // Open the standalone search window and run the query. On an exact name
        // match, RunQuery's completion path jumps straight to the info window.
        _searchWindow.IsOpen = true;
        _searchWindow.RunQuery(query);
    }
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
        _contextMenuService.Dispose();
        _hotkeyService.Dispose();
        _api.Dispose();
    }
}
