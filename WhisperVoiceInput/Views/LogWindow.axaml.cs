using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Serilog;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Views;

public partial class LogWindow : Window
{
    private readonly IClipboardService _clipboardService;
    public bool IsClosed { get; private set; }
    private ListBox? _list;
    private ScrollViewer? _scrollViewer;
    private double _lastExtentHeight = double.NaN;
    private bool _wasAtBottom = true;
    private readonly ILogger _logger;

    public LogWindow(IClipboardService clipboardService, ILogger logger)
    {
        _logger = logger.ForContext<LogWindow>();
        _clipboardService = clipboardService;
        InitializeComponent();
        this.Closed += (_, _) => IsClosed = true;
        this.Opened += OnOpened;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _list = this.FindControl<ListBox>("LogList");
        if (_list is null) return;
        _list.AttachedToVisualTree += (_, _) => HookScrollEvents();
        HookScrollEvents();
        // Autoscroll is now handled via attached behavior. Keep only selection clearing.
        _list.SelectionChanged += (_, _) =>
        {
            // Immediately clear selection to emulate non-selectable rows
            if (_list != null && _list.SelectedIndex != -1)
            {
                _list.SelectedIndex = -1;
            }
        };
    }

    private void HookScrollEvents()
    {
        if (_list is null) return;
        _scrollViewer = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scrollViewer == null) return;
        _scrollViewer.ScrollChanged += (_, _) =>
        {
            var extent = _scrollViewer.Extent;
            var viewport = _scrollViewer.Viewport;
            var offset = _scrollViewer.Offset;
            var extentChanged = !double.IsNaN(_lastExtentHeight) && Math.Abs(extent.Height - _lastExtentHeight) > 0.1;
            if (!extentChanged)
            {
                _wasAtBottom = offset.Y + viewport.Height >= extent.Height - 4;
            }
            _lastExtentHeight = extent.Height;
            Log.Debug("LogWindow ScrollChanged: extent={Extent}, viewport={Viewport}, offset={Offset}, wasAtBottom={Was}", extent, viewport, offset, _wasAtBottom);
        };
    }

    private void OnOpenLogsFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WhisperVoiceInput",
                "logs");

            if (!System.IO.Directory.Exists(logPath))
            {
                _ = _clipboardService.SetTextAsync("Logs folder not found: " + logPath);
                return;
            }

            using var p = new System.Diagnostics.Process();
            p.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            };
            p.Start();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open logs folder");
        }
    }
}




