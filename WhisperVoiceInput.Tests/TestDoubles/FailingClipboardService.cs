using Avalonia.Controls;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Clipboard service double that fails SetTextAsync for error-path testing.
/// </summary>
public class FailingClipboardService : IClipboardService
{
    public Exception Exception { get; init; } = new InvalidOperationException("Clipboard operation failed");

    public void SetTopLevel(TopLevel topLevel)
    {
    }

    public Task SetTextAsync(string text) => Task.FromException(Exception);
}
