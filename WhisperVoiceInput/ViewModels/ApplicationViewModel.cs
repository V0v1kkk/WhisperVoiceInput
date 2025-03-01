using System;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

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
    private readonly AudioRecordingService _recordingService;
    private TranscriptionService _transcriptionService;
    private readonly CommandSocketListener? _socketListener;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly SettingsService _settingsService;
    
    // State properties
    private TrayIconState _trayIconState;
    private string _errorMessage = string.Empty;
    private bool _isRecording;
    private bool _isProcessing;
    private DateTime? _lastStateChange;
    
    // Observable properties
    private readonly ObservableAsPropertyHelper<WindowIcon> _icon;
    private readonly ObservableAsPropertyHelper<string> _tooltipText;
    private readonly ObservableAsPropertyHelper<bool> _canToggleRecording;
    
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

    public bool IsRecording
    {
        get => _isRecording;
        private set => this.RaiseAndSetIfChanged(ref _isRecording, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
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
        SettingsService settingsService)
    {
        _lifetime = lifetime;
        _logger = logger.ForContext<ApplicationViewModel>();
        _mainWindowViewModel = mainWindowViewModel;
        _settingsService = settingsService;

        // Initialize services
        _recordingService = new AudioRecordingService(logger);
        _transcriptionService = new TranscriptionService(logger, _settingsService.CurrentSettings);

        
        // initialize socket listener only on Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var socketPath = "/tmp/WhisperVoiceInput/pipe";

            _socketListener = new CommandSocketListener(
                logger,
                socketPath,
                async () => await ToggleRecordingAsync());

            // Start socket listener
            _socketListener.Start();
        }
        
        // Set up state change handlers
        this.WhenAnyValue(x => x.IsRecording)
            .Subscribe(recording =>
            {
                if (recording)
                {
                    TrayIconState = TrayIconState.Recording;
                }
                else if (!IsProcessing)
                {
                    TrayIconState = TrayIconState.Idle;
                }
            });

        this.WhenAnyValue(x => x.IsProcessing)
            .Subscribe(processing =>
            {
                if (processing)
                {
                    TrayIconState = TrayIconState.Processing;
                }
                else if (!IsRecording)
                {
                    TrayIconState = TrayIconState.Idle;
                }
            });

        // Track state changes
        this.WhenAnyValue(x => x.TrayIconState)
            .Subscribe(_ => _lastStateChange = DateTime.Now);

        // Clear error message when state changes to non-error
        this.WhenAnyValue(x => x.TrayIconState)
            .Where(state => state != TrayIconState.Error)
            .Subscribe(_ => ErrorMessage = string.Empty);

        // Set up observable properties
        _tooltipText = this.WhenAnyValue(x => x.TrayIconState)
            .Select(state => state switch
            {
                TrayIconState.Error => $"Error: {ErrorMessage}",
                TrayIconState.Recording => "Recording in progress...",
                TrayIconState.Processing => "Processing audio...",
                TrayIconState.Success => "Transcription processed successfully!",
                _ => "WhisperVoiceInput"
            })
            .ToProperty(this, nameof(TooltipText), "WhisperVoiceInput");

        _icon = this.WhenAnyValue(x => x.TrayIconState)
            .Select(state => CreateTrayIcon(GetTrayIconColor(state)))
            .ToProperty(this, nameof(Icon));
            
        _canToggleRecording = this.WhenAnyValue(x => x.IsProcessing)
            .Select(isProcessing => !isProcessing)
            .ToProperty(this, nameof(_canToggleRecording));
        
        // Initialize commands
        ShowSettingsCommand = ReactiveCommand.Create(() => {}); // for subscription
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
        ExitCommand = ReactiveCommand.Create(ExitApplication);
        ToggleRecordingCommand = ReactiveCommand.CreateFromTask(
            ToggleRecordingAsync,
            this.WhenAnyValue(x => x.IsProcessing).Select(x => !x));
        
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
                        _lifetime.MainWindow = new Views.MainWindow
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
        
        // Subscribe to settings changes
        _settingsService.Settings
            .DistinctUntilChanged()
            .Subscribe(RecreateTranscriptionService);
    }

    private void RecreateTranscriptionService(AppSettings settings)
    {
        lock (this) // Ensure thread safety
        {
            var oldService = _transcriptionService;
            _transcriptionService = new TranscriptionService(_logger, settings);
            oldService?.Dispose();
        }
    }

    public void SetError(string message)
    {
        ErrorMessage = message;
        TrayIconState = TrayIconState.Error;
    }

    public void SetSuccess()
    {
        TrayIconState = TrayIconState.Success;
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
        var aboutWindow = new Views.AboutWindow();
        aboutWindow.Show();
        _logger.Information("About window shown");
    }

    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync();
        }
        else
        {
            _ = StartRecordingAsync(); // avoid await to prevent command blocking
        }
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            IsRecording = true;
            await _recordingService.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _logger.Error(ex, "Error starting recording");
            IsRecording = false;
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            var audioFilePath = await _recordingService.StopRecording();
            IsRecording = false;

            IsProcessing = true;
            var transcribedText = await _transcriptionService.TranscribeAudioAsync(audioFilePath);

            switch (_settingsService.OutputType)
            {
                case ResultOutputType.WlCopy:
                    await CopyToClipboardWaylandAsync(transcribedText);
                    break;
                case ResultOutputType.YdotoolType:
                    await TypeWithYdotoolAsync(transcribedText);
                    break;
                case ResultOutputType.WtypeType:
                    await TypeWithWtypeAsync(transcribedText);
                    break;
                default: // ClipboardAvaloniaApi
                    var topLevel = TopLevel.GetTopLevel(_lifetime.MainWindow);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(transcribedText);
                    }
                    else
                    {
                        _logger.Error("Could not access clipboard");
                    }
                    break;
            }

            SetSuccess();
            _logger.Information("Text processed successfully: {Text}", transcribedText);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _logger.Error(ex, "Error during recording/transcription process");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task CopyToClipboardWaylandAsync(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "wl-copy",
                Arguments = text,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                _logger.Error("wl-copy exited with code {ExitCode}", process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy to clipboard using wl-copy");
        }
    }

    private async Task TypeWithYdotoolAsync(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ydotool",
                Arguments = $"type -d 1 {text}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                _logger.Error("ydotool exited with code {ExitCode}", process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to type text using ydotool");
        }
    }

    private async Task TypeWithWtypeAsync(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "wtype",
                Arguments = $"\"{text}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                _logger.Error("wtype exited with code {ExitCode}", process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to type text using wtype");
        }
    }

    private void ExitApplication()
    {
        _recordingService.Dispose();
        _transcriptionService.Dispose();
        _socketListener?.Dispose();
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
                        Marshal.Copy(tempFb.Address, originalPixels, 0, originalPixels.Length);
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
                Marshal.Copy(newPixels, 0, fb.Address, newPixels.Length);
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
        _icon.Dispose();
        _tooltipText.Dispose();
        _canToggleRecording.Dispose();
        _recordingService.Dispose();
        _transcriptionService.Dispose();
        _socketListener?.Dispose();
    }
}