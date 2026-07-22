using System;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI.Avalonia;
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Extensions;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Views;
// ReSharper disable ExplicitCallerInfoArgument

namespace WhisperVoiceInput.ViewModels;

public enum TrayIconState
{
    Idle,
    Recording,
    Processing,
    Success,
    Error
}

public class ApplicationViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IRecordingToggler _recordingToggler;
    private readonly IPipelineController _pipelineController;
    private readonly IStateObservableFactory _stateObservableFactory;
    private readonly IClipboardService _clipboardService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILogBufferService _logBufferService;
    
    // Windows
    private Window? _settingsWindow;
    
    // State properties
    private TrayIconState _trayIconState;
    private string _errorMessage = string.Empty;
    private AppState _lastAppState = AppState.Idle;
    private bool _isReprocessAvailable;
    private string _reprocessHeader = "Reprocess";
    
    // Observable properties
    private readonly ObservableAsPropertyHelper<WindowIcon> _icon;
    private readonly ObservableAsPropertyHelper<string> _tooltipText;
    
    // Timer subscription for state transitions
    private IDisposable? _stateTransitionSubscription;
    
    // Public properties
    public WindowIcon Icon => _icon.Value;
    public string TooltipText => _tooltipText.Value;
    
    public TrayIconState TrayIconState
    {
        get => _trayIconState;
        private set => this.RaiseAndSetIfChanged(ref _trayIconState, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool IsPipelineBusy => _lastAppState != AppState.Idle;

    public bool IsReprocessAvailable
    {
        get => _isReprocessAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isReprocessAvailable, value);
    }

    public string ReprocessHeader
    {
        get => _reprocessHeader;
        private set => this.RaiseAndSetIfChanged(ref _reprocessHeader, value);
    }

    public bool IsReprocessVisible => _mainWindowViewModel.KeepLastRecordingInput;

    // Commands
    public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ReprocessCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowLogCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    public ApplicationViewModel(IClassicDesktopStyleApplicationLifetime lifetime,
        ILogger logger,
        MainWindowViewModel mainWindowViewModel,
        IRecordingToggler recordingToggler,
        IPipelineController pipelineController,
        IStateObservableFactory stateObservableFactory,
        IClipboardService clipboardService, 
        IGlobalHotkeyService globalHotkeyService,
        ILogBufferService logBufferService)
    {
        _lifetime = lifetime;
        _logger = logger.ForContext<ApplicationViewModel>();
        _mainWindowViewModel = mainWindowViewModel;
        _recordingToggler = recordingToggler;
        _pipelineController = pipelineController;
        _stateObservableFactory = stateObservableFactory;
        _clipboardService = clipboardService;
        _hotkeyService = globalHotkeyService;
        _logBufferService = logBufferService;

        InitializeGlobalHotkey();
        
        // Subscribe to state changes from actor system
        _stateObservableFactory.GetStateObservable()
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(HandleStateUpdate);

        // Subscribe to reprocess availability from actor system
        _stateObservableFactory.GetReprocessAvailableObservable()
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(evt => IsReprocessAvailable = evt.IsAvailable);

        // Clear error message when state changes to non-error
        this.WhenAnyValue(x => x.TrayIconState)
            .Where(state => state != TrayIconState.Error)
            .Subscribe(_ => ErrorMessage = string.Empty);

        // Update reprocess visibility and header based on conditions
        _mainWindowViewModel.WhenAnyValue(x => x.KeepLastRecordingInput)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsReprocessVisible)));

        this.WhenAnyValue(x => x.IsReprocessAvailable)
            .Subscribe(available =>
            {
                ReprocessHeader = available
                    ? "Reprocess"
                    : "Reprocess (no recording)";
            });

        // Set up observable properties
        _tooltipText = this.WhenAnyValue(x => x.TrayIconState, x => x.ErrorMessage)
            .Select(tuple => tuple.Item1 switch
            {
                TrayIconState.Error => $"Error: {tuple.Item2}",
                TrayIconState.Recording => "Recording in progress...",
                TrayIconState.Processing => "Processing audio...",
                TrayIconState.Success => "Transcription processed successfully!",
                _ => "WhisperVoiceInput"
            })
            .ToProperty(this, nameof(TooltipText), "WhisperVoiceInput");

        _icon = this.WhenAnyValue(x => x.TrayIconState)
            .Select(state => this.Memoized(state, iconState => CreateTrayIcon(GetTrayIconColor(iconState)), cacheKey: "CreateTrayIcon"))
            .ToProperty(this, nameof(Icon));

        // Cancel command: enabled only when pipeline is busy
        var canCancel = this.WhenAnyValue(x => x.IsPipelineBusy);
        CancelCommand = ReactiveCommand.Create(() =>
        {
            if (!IsPipelineBusy) return;
            _pipelineController.CancelPipeline();
        }, canCancel);

        // Reprocess command: enabled when idle + file available + setting on
        var canReprocess = this.WhenAnyValue(
                x => x.IsPipelineBusy,
                x => x.IsReprocessAvailable,
                x => x._mainWindowViewModel.KeepLastRecordingInput,
                (busy, available, keepEnabled) => !busy && available && keepEnabled);
        ReprocessCommand = ReactiveCommand.Create(() =>
        {
            if (IsPipelineBusy || !IsReprocessAvailable || !_mainWindowViewModel.KeepLastRecordingInput) return;
            _pipelineController.Reprocess();
        }, canReprocess);
        
        // Initialize commands
        ShowSettingsCommand = ReactiveCommand.Create(ToggleSettingsWindow);
        ShowSettingsCommand.ThrownExceptions.Subscribe(ex =>
            _logger.Error(ex, "ShowSettingsCommand threw an exception"));
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
        ShowLogCommand = ReactiveCommand.Create(ShowLogWindow);
        ExitCommand = ReactiveCommand.Create(ExitApplication);
        ToggleRecordingCommand = ReactiveCommand.Create(() => _recordingToggler.ToggleRecording());

    }

    private void ToggleSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new MainWindow
        {
            DataContext = _mainWindowViewModel,
        };
        _clipboardService.SetTopLevel(_settingsWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private LogWindow? _logWindow;
    private void ShowLogWindow()
    {
        if (_logWindow == null || _logWindow.IsClosed)
        {
            _logWindow = new LogWindow(_clipboardService, _logger)
            {
                DataContext = new LogWindowViewModel(_logBufferService)
            };
            _clipboardService.SetTopLevel(_logWindow);
            _logWindow.Show();
        }
        else
        {
            _logWindow.Activate();
        }
    }

    private void InitializeGlobalHotkey()
    {
        try
        {
            var settings = _mainWindowViewModel;
            if (settings.GlobalHotkeyEnabledInput && !string.IsNullOrWhiteSpace(settings.GlobalHotkeyInput))
            {
                _hotkeyService.UpdateBinding(settings.GlobalHotkeyInput, () => _recordingToggler.ToggleRecording());
                _hotkeyService.Start();
            }

            // React to changes
            settings.WhenAnyValue(x => x.GlobalHotkeyEnabledInput, x => x.GlobalHotkeyInput)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .Subscribe(tuple =>
                {
                    var (enabled, hotkey) = tuple;
                    if (!enabled || string.IsNullOrWhiteSpace(hotkey))
                    {
                        _hotkeyService.Stop();
                    }
                    else
                    {
                        _hotkeyService.UpdateBinding(hotkey, () => _recordingToggler.ToggleRecording());
                        _hotkeyService.Start();
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to initialize global hotkeys; continuing without them");
        }
    }

    private void HandleStateUpdate(StateUpdatedEvent stateEvent)
    {
        _lastAppState = stateEvent.State;
        this.RaisePropertyChanged(nameof(IsPipelineBusy));

        switch (stateEvent.State)
        {
            case AppState.Idle:
                if (_stateTransitionSubscription == null)
                    TrayIconState = TrayIconState.Idle;
                break;
                
            case AppState.Recording:
                _stateTransitionSubscription?.Dispose();
                _stateTransitionSubscription = null;
                TrayIconState = TrayIconState.Recording;
                break;
                
            case AppState.Transcribing:
            case AppState.PostProcessing:
                _stateTransitionSubscription?.Dispose();
                _stateTransitionSubscription = null;
                TrayIconState = TrayIconState.Processing;
                break;

            case AppState.Saving:
                _stateTransitionSubscription?.Dispose();
                _stateTransitionSubscription = null;
                TrayIconState = TrayIconState.Processing;
                break;
                
            case AppState.Error:
                ErrorMessage = stateEvent.ErrorMessage ?? "Unknown error occurred";
                TrayIconState = TrayIconState.Error;
                
                _stateTransitionSubscription?.Dispose();
                _stateTransitionSubscription = Observable.Timer(TimeSpan.FromSeconds(5))
                    .ObserveOn(AvaloniaScheduler.Instance)
                    .Subscribe(_ =>
                    {
                        if (TrayIconState == TrayIconState.Error)
                        {
                            TrayIconState = TrayIconState.Idle;
                        }
                        _stateTransitionSubscription?.Dispose();
                        _stateTransitionSubscription = null;
                    });
                break;
                
            case AppState.Success:
                TrayIconState = TrayIconState.Success;
                
                _stateTransitionSubscription?.Dispose();
                _stateTransitionSubscription = Observable.Timer(TimeSpan.FromSeconds(5))
                    .ObserveOn(AvaloniaScheduler.Instance)
                    .Subscribe(_ =>
                    {
                        if (TrayIconState == TrayIconState.Success)
                        {
                            TrayIconState = TrayIconState.Idle;
                        }
                        _stateTransitionSubscription?.Dispose();
                        _stateTransitionSubscription = null;
                    });
                break;
        }
    }

    private static Color GetTrayIconColor(TrayIconState state)
    {
        return state switch
        {
            TrayIconState.Idle => Colors.White,
            TrayIconState.Recording => Colors.Yellow,
            TrayIconState.Processing => Colors.LightBlue,
            TrayIconState.Success => Colors.Green,
            TrayIconState.Error => Colors.Red,
            _ => Colors.White
        };
    }

    private void ShowAbout()
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.Show();
        _logger.Information("About window shown");
    }

    private void ExitApplication()
    {
        _lifetime.Shutdown();
    }

    private WindowIcon CreateTrayIcon(Color color)
    {
        // Load the white icon
        var uri = new Uri("avares://WhisperVoiceInput/Assets/lecturer-white.png");
        using var iconStream = AssetLoader.Open(uri);
        var originalBitmap = new Bitmap(iconStream);
        
        try
        {
            // Get the original bitmap's DPI and dimensions
            var originalDpi = originalBitmap.Dpi;
            
            // Create a writeable bitmap with the same dimensions and DPI
            var writeableBitmap = new WriteableBitmap(
                originalBitmap.PixelSize,
                originalDpi,
                PixelFormats.Bgra8888,
                AlphaFormat.Premul);
            
            // Lock the bitmap for writing
            using (var fb = writeableBitmap.Lock())
            {
                // Get the stride (bytes per row)
                var stride = fb.RowBytes;
                var width = fb.Size.Width;
                var height = fb.Size.Height;
                
                // Create byte arrays for the original and new pixels
                var originalPixels = new byte[stride * height];
                var newPixels = new byte[stride * height];
                
                // Create a temporary bitmap to read the original pixels
                using (var tempBitmap = new WriteableBitmap(
                    originalBitmap.PixelSize,
                    originalDpi,
                    PixelFormats.Bgra8888,
                    AlphaFormat.Premul))
                {
                    // Draw the original bitmap onto the temporary bitmap
                    using (var tempFb = tempBitmap.Lock())
                    {
                        // Copy the original bitmap to the temporary bitmap
                        originalBitmap.CopyPixels(
                            new PixelRect(0, 0, width, height),
                            tempFb.Address,
                            stride * height,
                            stride);
                        
                        // Copy the pixels from the temporary bitmap
                        System.Runtime.InteropServices.Marshal.Copy(tempFb.Address, originalPixels, 0, originalPixels.Length);
                    }
                }
                
                // Process each pixel
                for (int i = 0; i < originalPixels.Length; i += 4)
                {
                    // Get the color components (BGRA format)
                    byte b = originalPixels[i];
                    byte g = originalPixels[i + 1];
                    byte r = originalPixels[i + 2];
                    byte a = originalPixels[i + 3];
                    
                    // If the pixel is white or close to white (with some tolerance)
                    if (r > 200 && g > 200 && b > 200 && a > 0)
                    {
                        // Replace with target color while preserving alpha
                        newPixels[i] = color.B;
                        newPixels[i + 1] = color.G;
                        newPixels[i + 2] = color.R;
                        newPixels[i + 3] = a;
                    }
                    else
                    {
                        // Keep the original pixel
                        newPixels[i] = b;
                        newPixels[i + 1] = g;
                        newPixels[i + 2] = r;
                        newPixels[i + 3] = a;
                    }
                }
                
                // Copy the modified pixels to the writeable bitmap
                System.Runtime.InteropServices.Marshal.Copy(newPixels, 0, fb.Address, newPixels.Length);
            }
            
            return new WindowIcon(writeableBitmap);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error creating colored icon");
            return new WindowIcon(originalBitmap);
        }
    }
    
    public override void Dispose()
    {
        base.Dispose();
        _stateTransitionSubscription?.Dispose();
        _icon.Dispose();
        _tooltipText.Dispose();
        CancelCommand.Dispose();
        ReprocessCommand.Dispose();
    }
}
