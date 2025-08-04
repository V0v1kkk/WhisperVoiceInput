using Avalonia;
using Avalonia.Controls;
using System;
using System.Threading.Tasks;
using Serilog;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Services;

/// <summary>
/// Avalonia-based implementation of IClipboardService.
/// Uses the notification window or main window for clipboard access.
/// </summary>
public class ClipboardService : IClipboardService
{
    private readonly ILogger _logger;
    private TopLevel? _topLevel;

    public ClipboardService(ILogger logger)
    {
        _logger = logger.ForContext<ClipboardService>();
    }

    /// <summary>
    /// Sets the TopLevel (window) to use for clipboard operations.
    /// This should be called from the UI thread when a window is available.
    /// </summary>
    /// <param name="topLevel">The TopLevel (window) to use for clipboard access</param>
    public void SetTopLevel(TopLevel topLevel)
    {
        _topLevel = topLevel;
        _logger.Debug("TopLevel set for clipboard operations");
    }

    public async Task SetTextAsync(string text)
    {
        if (_topLevel?.Clipboard == null)
        {
            _logger.Error("No TopLevel or Clipboard available for clipboard operation");
            throw new InvalidOperationException("Clipboard not available. Ensure SetTopLevel() is called with a valid window.");
        }

        try
        {
            await _topLevel.Clipboard.SetTextAsync(text);
            _logger.Debug("Text copied to clipboard successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy text to clipboard");
            throw;
        }
    }
}