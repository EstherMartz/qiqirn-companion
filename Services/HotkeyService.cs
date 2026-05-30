using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace QiqirnCompanion.Services;

/// <summary>
/// Owns all game-keyboard reading. Runs each framework tick: edge-detects the
/// user's configured combo to toggle the main window, and handles press-to-bind
/// capture when the Settings panel requests it. Dalamud has no hotkey registry,
/// so this polls <see cref="IKeyState"/> from <see cref="IFramework"/>.Update.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private static readonly HashSet<VirtualKey> Modifiers = new()
    {
        VirtualKey.CONTROL, VirtualKey.LCONTROL, VirtualKey.RCONTROL,
        VirtualKey.SHIFT,   VirtualKey.LSHIFT,   VirtualKey.RSHIFT,
        VirtualKey.MENU,    VirtualKey.LMENU,    VirtualKey.RMENU,
        VirtualKey.LWIN,    VirtualKey.RWIN,
    };

    private readonly IFramework    _framework;
    private readonly IKeyState     _keyState;
    private readonly Configuration _config;
    private readonly Action        _toggleMain;

    private bool _wasComboDown;

    public bool IsCapturing { get; private set; }

    public HotkeyService(IFramework framework, IKeyState keyState, Configuration config, Action toggleMain)
    {
        _framework  = framework;
        _keyState   = keyState;
        _config     = config;
        _toggleMain = toggleMain;
        _framework.Update += OnUpdate;
    }

    /// <summary>Enter listen mode; the next non-modifier key press becomes the binding.</summary>
    public void BeginCapture()  => IsCapturing = true;
    public void CancelCapture() => IsCapturing = false;

    private void OnUpdate(IFramework framework)
    {
        if (IsCapturing)
        {
            TryCapture();
            return;
        }

        if (_config.HotkeyKey == VirtualKey.NO_KEY || ImGui.GetIO().WantTextInput)
        {
            _wasComboDown = false;
            return;
        }

        var down = _keyState[_config.HotkeyKey]
            && (!_config.HotkeyCtrl  || _keyState[VirtualKey.CONTROL])
            && (!_config.HotkeyAlt   || _keyState[VirtualKey.MENU])
            && (!_config.HotkeyShift || _keyState[VirtualKey.SHIFT]);

        if (down && !_wasComboDown)
            _toggleMain();
        _wasComboDown = down;
    }

    private void TryCapture()
    {
        if (_keyState[VirtualKey.ESCAPE])
        {
            IsCapturing = false;
            return;
        }

        foreach (var vk in _keyState.GetValidVirtualKeys())
        {
            if (Modifiers.Contains(vk) || vk == VirtualKey.ESCAPE) continue;
            if (!_keyState[vk]) continue;

            _config.HotkeyKey   = vk;
            _config.HotkeyCtrl  = _keyState[VirtualKey.CONTROL];
            _config.HotkeyAlt   = _keyState[VirtualKey.MENU];
            _config.HotkeyShift = _keyState[VirtualKey.SHIFT];
            _config.Save();
            IsCapturing = false;
            return;
        }
    }

    public void Dispose() => _framework.Update -= OnUpdate;
}
