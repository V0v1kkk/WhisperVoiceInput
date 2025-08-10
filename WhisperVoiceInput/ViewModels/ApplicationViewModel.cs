using System;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
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
    private readonly IStateObservableFactory _stateObservableFactory;
    private readonly IClipboardService _clipboardService;
    
    // Windows
    private NotificationWindow? _notificationWindow;
    private NotificationWindowViewModel? _notificationWindowViewModel;
    
    // State properties
    private TrayIconState _trayIconState;
    private string _errorMessage = string.Empty;
    
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

    // Commands
    public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    public ApplicationViewModel(
        IClassicDesktopStyleApplicationLifetime lifetime,
        ILogger logger,
        MainWindowViewModel mainWindowViewModel,
        IRecordingToggler recordingToggler,
        IStateObservableFactory stateObservableFactory,
        IClipboardService clipboardService)
    {
        _lifetime = lifetime;
        _logger = logger.ForContext<ApplicationViewModel>();
        _mainWindowViewModel = mainWindowViewModel;
        _recordingToggler = recordingToggler;
        _stateObservableFactory = stateObservableFactory;
        _clipboardService = clipboardService;

        // Initialize notification window and set up clipboard service
        InitializeNotificationWindow();
        
        // Subscribe to state changes from actor system
        _stateObservableFactory.GetStateObservable()
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(HandleStateUpdate);

        // Clear error message when state changes to non-error
        this.WhenAnyValue(x => x.TrayIconState)
            .Where(state => state != TrayIconState.Error)
            .Subscribe(_ => ErrorMessage = string.Empty);

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
        
        // Initialize commands
        ShowSettingsCommand = ReactiveCommand.Create(() => {}); // for subscription
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
        ExitCommand = ReactiveCommand.Create(ExitApplication);
        ToggleRecordingCommand = ReactiveCommand.Create(() => _recordingToggler.ToggleRecording());
        
        // Initialize command subscriptions
        ShowSettingsCommand
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(_ =>
            {
                var currentlyVisible = _lifetime.MainWindow?.IsVisible ?? false;
                
                if (!currentlyVisible)
                {
                    if (_lifetime.MainWindow == null)
                    {
                        _lifetime.MainWindow = new MainWindow
                        {
                            DataContext = _mainWindowViewModel,
                            IsVisible = true
                        };
                    }
                    else
                    {
                        _lifetime.MainWindow.IsVisible = true;
                    }
                }
                else
                {
                    _lifetime.MainWindow?.Hide();
                }
            });

    }

    private void HandleStateUpdate(StateUpdatedEvent stateEvent)
    {
        switch (stateEvent.State)
        {
            case AppState.Idle:
                if (_stateTransitionSubscription == null)
                    TrayIconState = TrayIconState.Idle;
                break;
                
            case AppState.Recording:
                TrayIconState = TrayIconState.Recording;
                break;
                
            case AppState.Transcribing:
            case AppState.PostProcessing:
                TrayIconState = TrayIconState.Processing;
                break;
                
            case AppState.Error:
                ErrorMessage = stateEvent.ErrorMessage ?? "Unknown error occurred";
                TrayIconState = TrayIconState.Error;
                
                // Schedule transition back to Idle after showing error (with disposal handling)
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
                
                // Schedule transition back to Idle after showing success (with disposal handling)
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

    private void InitializeNotificationWindow()
    {
        _notificationWindow = new NotificationWindow();
        _notificationWindowViewModel = new NotificationWindowViewModel(_notificationWindow, _logger);
        _notificationWindow.DataContext = _notificationWindowViewModel;
        
        // Show the notification window and set up clipboard service TopLevel
        _notificationWindow.Show();
        _clipboardService.SetTopLevel(_notificationWindow);
        _logger.Information("Notification window shown and clipboard service configured");
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
        _notificationWindowViewModel?.Dispose();
        _notificationWindow?.Close();
    }
}
