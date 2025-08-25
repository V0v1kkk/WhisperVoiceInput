using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WhisperVoiceInput.Views.Controls;

public partial class HotkeyTextBox : UserControl
{
	public static readonly StyledProperty<string> HotkeyProperty =
		AvaloniaProperty.Register<HotkeyTextBox, string>(nameof(Hotkey), string.Empty, defaultBindingMode: BindingMode.TwoWay);

	public string Hotkey
	{
		get => GetValue(HotkeyProperty);
		set => SetValue(HotkeyProperty, value);
	}

	public event Action<string>? HotkeyChanged;

	public HotkeyTextBox()
	{
		InitializeComponent();
		Input.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
		Input.GotFocus += (_, _) => Input.Watermark = "Press keys...";
		Input.LostFocus += (_, _) => Input.Watermark = "Click and press keys";
		this.GetObservable(HotkeyProperty).Subscribe(text => Input.Text = text ?? string.Empty);
	}

	private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
	{
		e.Handled = true;

		// Translate modifiers
		var parts = new System.Collections.Generic.List<string>();
		var mods = e.KeyModifiers;
		if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
		if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
		if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
		if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");

		var key = e.Key == Key.System ? e.PhysicalKey.ToString() : e.Key.ToString();
		// Ignore pure modifier presses
		if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
			return;

		// Reset
		if (mods == KeyModifiers.None && (e.Key is Key.Delete or Key.Back or Key.Escape))
		{
			Hotkey = string.Empty;
			HotkeyChanged?.Invoke(string.Empty);
			return;
		}

		parts.Add(NormalizeKeyName(key));
		var text = string.Join("+", parts);
		Hotkey = text;
		HotkeyChanged?.Invoke(text);
	}

	private static string NormalizeKeyName(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return string.Empty;
		if (name.StartsWith("D") && name.Length == 2 && char.IsDigit(name[1]))
			return name[1].ToString();
		return name switch
		{
			"Return" => "Enter",
			_ => name
		};
	}

	public void SetText(string text)
	{
		Hotkey = text;
	}
}

