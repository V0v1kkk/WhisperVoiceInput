using System;

namespace WhisperVoiceInput.Services;

public static class DisplayServerDetector
{
	public static bool IsWayland()
	{
		var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
		if (!string.IsNullOrEmpty(sessionType) && sessionType.Equals("wayland", StringComparison.OrdinalIgnoreCase))
			return true;

		var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
		if (!string.IsNullOrEmpty(waylandDisplay))
			return true;

		return false;
	}

	public static bool IsX11()
	{
		var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
		if (!string.IsNullOrEmpty(sessionType) && sessionType.Equals("x11", StringComparison.OrdinalIgnoreCase))
			return true;

		var display = Environment.GetEnvironmentVariable("DISPLAY");
		return !string.IsNullOrEmpty(display) && sessionType == null;
	}
}

