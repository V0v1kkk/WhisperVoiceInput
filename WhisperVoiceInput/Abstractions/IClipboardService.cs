using System.Threading.Tasks;
using Avalonia.Controls;

namespace WhisperVoiceInput.Abstractions;

/// <summary>
/// Interface for clipboard operations.
/// Abstracts the Avalonia clipboard dependency from actors.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Optionally overrides the platform clipboard with a TopLevel instance.
    /// When not called, the platform clipboard resolved at runtime is used.
    /// </summary>
    /// <param name="topLevel">The TopLevel (window) to use for clipboard access</param>
    void SetTopLevel(TopLevel topLevel);

    /// <summary>
    /// Sets text to the clipboard using Avalonia API.
    /// </summary>
    /// <param name="text">The text to copy to clipboard</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task SetTextAsync(string text);
}