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
    /// Sets the TopLevel (window) to use for clipboard operations.
    /// This should be called from the UI thread when a window is available.
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