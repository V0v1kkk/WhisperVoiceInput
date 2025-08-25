using System;

namespace WhisperVoiceInput.Abstractions;

public interface IGlobalHotkeyService : IDisposable
{
	void Start();
	void Stop();
	void UpdateBinding(string hotkeyText, Action callback);
}

