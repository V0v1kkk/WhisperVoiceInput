using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Mock clipboard service for testing
/// </summary>
public class MockClipboardService : IClipboardService
{
    public void SetTopLevel(Avalonia.Controls.TopLevel topLevel)
    {
        // Mock implementation - do nothing
    }

    public Task SetTextAsync(string text)
    {
        // Mock implementation - just return completed task
        return Task.CompletedTask;
    }
}