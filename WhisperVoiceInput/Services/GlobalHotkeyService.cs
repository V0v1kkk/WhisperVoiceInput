using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using SharpHook;
using SharpHook.Data;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
	private readonly ILogger _logger;
    private readonly TaskPoolGlobalHook _hook;
	private readonly HashSet<KeyCode> _pressed = new();
	private string _currentHotkeyText = string.Empty;
	private Action? _callback;
	private bool _running;

	public GlobalHotkeyService(ILogger logger)
	{
		_logger = logger.ForContext<GlobalHotkeyService>();
		_hook = new TaskPoolGlobalHook();
		_hook.KeyPressed += OnKeyPressed;
		_hook.KeyReleased += OnKeyReleased;
	}

    public void Start()
	{
		if (_running) return;
		// Do not start on Wayland
		if (OperatingSystem.IsLinux() && DisplayServerDetector.IsWayland())
		{
			_logger.Information("Wayland session detected - global hotkeys disabled");
			return;
		}
		_running = true;
		_hook.RunAsync();
		_logger.Information("Global hotkey service started");
	}

    public void Stop()
	{
		// No-op: keep hook running to allow fast rebind; just clear binding
		_currentHotkeyText = string.Empty;
		_callback = null;
	}

	public void UpdateBinding(string hotkeyText, Action callback)
	{
		_currentHotkeyText = hotkeyText ?? string.Empty;
		_callback = callback;
		_logger.Information("Updated global hotkey binding to {Hotkey}", _currentHotkeyText);
	}

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
	{
		_pressed.Add(e.Data.KeyCode);
		if (IsHotkeyCurrentlyPressed())
		{
			_callback?.Invoke();
		}
	}

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
	{
		_pressed.Remove(e.Data.KeyCode);
	}

	private bool IsHotkeyCurrentlyPressed()
	{
		if (string.IsNullOrWhiteSpace(_currentHotkeyText)) return false;

		var parsed = ParseHotkey(_currentHotkeyText);
		if (parsed.MainKey == KeyCode.VcUndefined) return false;

		if (!_pressed.Contains(parsed.MainKey)) return false;

		foreach (var m in parsed.Modifiers)
		{
			if (!IsEitherModifierPressed(m)) return false;
		}

		return true;
	}

    private bool IsEitherModifierPressed(KeyCode leftOrRight)
    {
        return leftOrRight switch
        {
            KeyCode.VcLeftControl => _pressed.Contains(KeyCode.VcLeftControl) || _pressed.Contains(KeyCode.VcRightControl),
            KeyCode.VcLeftShift => _pressed.Contains(KeyCode.VcLeftShift) || _pressed.Contains(KeyCode.VcRightShift),
            KeyCode.VcLeftAlt => _pressed.Contains(KeyCode.VcLeftAlt) || _pressed.Contains(KeyCode.VcRightAlt),
            KeyCode.VcLeftMeta => _pressed.Contains(KeyCode.VcLeftMeta) || _pressed.Contains(KeyCode.VcRightMeta),
            _ => false
        };
    }

	private (KeyCode MainKey, List<KeyCode> Modifiers) ParseHotkey(string text)
	{
		// Expect something like "Ctrl+Alt+W" or "Shift+F9"
		var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var modifiers = new List<KeyCode>();
		KeyCode main = KeyCode.VcUndefined;
		foreach (var p in parts)
		{
			var token = p.ToLowerInvariant();
			switch (token)
			{
				case "ctrl":
				case "control":
					modifiers.Add(KeyCode.VcLeftControl);
					break;
				case "shift":
					modifiers.Add(KeyCode.VcLeftShift);
					break;
				case "alt":
					modifiers.Add(KeyCode.VcLeftAlt);
					break;
				case "win":
				case "meta":
					modifiers.Add(KeyCode.VcLeftMeta);
					break;
				default:
					main = MapKeyToken(token);
					break;
			}
		}
		return (main, modifiers);
	}

	private static KeyCode MapKeyToken(string token)
	{
        if (token.Length == 1)
		{
			char c = token[0];
			if (c is >= 'a' and <= 'z')
                return (KeyCode)((int)KeyCode.VcA + (c - 'a'));
			if (c is >= '0' and <= '9')
                return (KeyCode)((int)KeyCode.Vc0 + (c - '0'));
		}
		return token switch
		{
			"space" => KeyCode.VcSpace,
			"enter" => KeyCode.VcEnter,
			"tab" => KeyCode.VcTab,
			"esc" or "escape" => KeyCode.VcEscape,
			_ => MapFunctionKey(token)
		};
	}

	private static KeyCode MapFunctionKey(string token)
	{
        if (token.StartsWith("f", StringComparison.OrdinalIgnoreCase) && int.TryParse(token.AsSpan(1), out var n) && n is >= 1 and <= 24)
		{
            return (KeyCode)((int)KeyCode.VcF1 + (n - 1));
		}
		return KeyCode.VcUndefined;
	}

    public void Dispose()
	{
		_hook.Dispose();
	}
}

