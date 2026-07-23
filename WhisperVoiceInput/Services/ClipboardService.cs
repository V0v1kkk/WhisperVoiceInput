using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Serilog;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Services;

/// <summary>
/// Avalonia-based implementation of IClipboardService.
/// Uses the platform clipboard registered during Avalonia startup (Wayland/X11/Win32),
/// with an optional TopLevel override when a window is available.
/// </summary>
public class ClipboardService : IClipboardService
{
    private readonly ILogger _logger;
    private readonly Func<IClipboard?> _platformClipboardProvider;
    private IClipboard? _clipboardOverride;

    public ClipboardService(ILogger logger, Func<IClipboard?>? platformClipboardProvider = null)
    {
        _logger = logger.ForContext<ClipboardService>();
        _platformClipboardProvider = platformClipboardProvider ?? TryGetPlatformClipboard;
    }

    /// <summary>
    /// Optional override for clipboard access via a visible TopLevel.
    /// The platform clipboard is used when this is not set.
    /// </summary>
    public void SetTopLevel(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);

        if (topLevel.Clipboard == null)
        {
            _logger.Warning("TopLevel.Clipboard is null; falling back to platform clipboard resolver");
            _clipboardOverride = null;
            return;
        }

        _clipboardOverride = topLevel.Clipboard;
        _logger.Debug("TopLevel clipboard override set");
    }

    public async Task SetTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clipboard = _clipboardOverride ?? _platformClipboardProvider();
        if (clipboard == null)
        {
            _logger.Error(
                "Platform clipboard service is not available (TopLevel override: {HasTopLevelOverride})",
                _clipboardOverride != null);
            throw new InvalidOperationException(
                "Clipboard not available. Ensure Avalonia platform initialization completed.");
        }

        try
        {
            // Avalonia clipboard on Win32/macOS requires UI thread affinity.
            // Akka actors run on thread-pool threads, so dispatch to UI thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                await clipboard.SetTextAsync(text);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await clipboard.SetTextAsync(text);
                });
            }
            _logger.Debug("Text copied to clipboard successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy text to clipboard");
            throw;
        }
    }

    /// <summary>
    /// Resolves IClipboard from Avalonia's service locator.
    /// AvaloniaLocator.Current is not part of the public compile-time API in 12.x,
    /// but the platform registers IClipboard there during startup.
    /// </summary>
    private IClipboard? TryGetPlatformClipboard()
    {
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            var locatorType = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator");
            if (locatorType == null)
            {
                _logger.Warning("AvaloniaLocator type not found; platform clipboard unavailable");
                return null;
            }

            var currentProperty = locatorType.GetProperty("Current", flags);
            if (currentProperty?.GetValue(null) is not { } current)
            {
                _logger.Warning("AvaloniaLocator.Current is null; platform clipboard unavailable");
                return null;
            }

            var getService = current.GetType().GetMethod(
                "GetService",
                BindingFlags.Public | BindingFlags.Instance,
                [typeof(Type)]);
            if (getService == null)
            {
                _logger.Warning("AvaloniaLocator.GetService method not found; platform clipboard unavailable");
                return null;
            }

            if (getService.Invoke(current, [typeof(IClipboard)]) is not IClipboard clipboard)
            {
                _logger.Warning("IClipboard is not registered in Avalonia service locator");
                return null;
            }

            return clipboard;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to resolve platform clipboard via AvaloniaLocator reflection");
            return null;
        }
    }
}
