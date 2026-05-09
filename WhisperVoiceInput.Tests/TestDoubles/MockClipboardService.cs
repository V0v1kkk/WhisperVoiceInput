using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Mock clipboard service for testing
/// </summary>
public class MockClipboardService : IClipboardService
{
    public int SetTextCallCount { get; private set; }
    public string? LastText { get; private set; }

    public void SetTopLevel(Avalonia.Controls.TopLevel topLevel)
    {
    }

    public Task SetTextAsync(string text)
    {
        SetTextCallCount++;
        LastText = text;
        return Task.CompletedTask;
    }
}