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
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.ViewModels;

public class ApplicationViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly AudioRecordingService _recordingService;
    private readonly TranscriptionService _transcriptionService;
    private readonly CommandSocketListener _socketListener;
    private readonly MainWindowViewModel _mainWindowViewModel;
    
    private readonly AppState _appState;
    
    
    private WindowIcon? _currentIcon;
    public WindowIcon Icon => _currentIcon ??= CreateTrayIcon(_appState.GetTrayIconColor());
    
    private bool _mainWindowIsVisible;
    public bool MainWindowIsVisible
    {
        get => _mainWindowIsVisible;
        set => this.RaiseAndSetIfChanged(ref _mainWindowIsVisible, value);
    }

    private string _tooltipText;
    public string TooltipText
    {
        get => _tooltipText;
        set => this.RaiseAndSetIfChanged(ref _tooltipText, value);
    }

    public AppState AppState => _appState;

    public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    public ApplicationViewModel(
        IClassicDesktopStyleApplicationLifetime lifetime, 
        ILogger logger,
        MainWindowViewModel mainWindowViewModel)
    {
        _lifetime = lifetime;
        _logger = logger;
        _mainWindowViewModel = mainWindowViewModel;
        _appState = new AppState();
        _tooltipText = "WhisperVoiceInput";
        
        _lifetime.MainWindow = new Views.MainWindow
        {
            DataContext = _mainWindowViewModel,
            IsVisible = false
        };

        // Initialize services
        _recordingService = new AudioRecordingService(logger);
        _transcriptionService = new TranscriptionService(logger, _mainWindowViewModel.GetCurrentSettings());

        var socketPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperVoiceInput",
            "command.sock");

        _socketListener = new CommandSocketListener(
            logger,
            socketPath,
            async () => await ToggleRecordingAsync());

        // Start socket listener
        _socketListener.Start();

        // Set up state change handling
        this.WhenAnyValue(x => x.AppState.TrayIconState)
            .Select(state => state switch
            {
                TrayIconState.Error => $"Error: {_appState.ErrorMessage}",
                TrayIconState.Recording => "Recording in progress...",
                TrayIconState.Processing => "Processing audio...",
                TrayIconState.Success => "Transcription processed successfully!",
                _ => "WhisperVoiceInput"
            })
            .Subscribe(text => TooltipText = text);

        // Update icon when state changes
        this.WhenAnyValue(x => x.AppState.TrayIconState)
            .Select(state => CreateTrayIcon(_appState.GetTrayIconColor()))
            .Subscribe(icon => _currentIcon = icon);
        
        this.WhenAnyValue(x => x.MainWindowIsVisible)
            .Subscribe(visible =>
            {
                if (visible)
                {
                    if (_lifetime.MainWindow != null)
                    {
                        _lifetime.MainWindow.IsVisible = true;
                    }
                }
                else
                {
                    _lifetime.MainWindow?.Hide();
                }
            });

        // Initialize commands
        ToggleRecordingCommand = ReactiveCommand.CreateFromTask(
            ToggleRecordingAsync,
            this.WhenAnyValue(x => x.AppState.IsProcessing).Select(x => !x));

        ShowSettingsCommand = ReactiveCommand.Create<Unit>(_ => MainWindowIsVisible = !MainWindowIsVisible);
        ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
        ExitCommand = ReactiveCommand.Create(ExitApplication);
    }

    

    private void ShowAbout()
    {
        // TODO: Implement About window
        _logger.Information("About window requested");
    }

    private async Task ToggleRecordingAsync()
    {
        if (_appState.IsRecording)
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
            _appState.IsRecording = true;
            await _recordingService.StartRecordingAsync();
        }
        catch (Exception ex)
        {
            _appState.SetError(ex.Message);
            _logger.Error(ex, "Error starting recording");
            _appState.IsRecording = false;
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            var audioFilePath = await _recordingService.StopRecording();
            _appState.IsRecording = false;

            _appState.IsProcessing = true;
            var transcribedText = await _transcriptionService.TranscribeAudioAsync(audioFilePath);

            var settings = _mainWindowViewModel.GetCurrentSettings();
            switch (settings.OutputType)
            {
                case ResultOutputType.WlCopy:
                    await CopyToClipboardWaylandAsync(transcribedText);
                    break;
                case ResultOutputType.YdotoolType:
                    await TypeWithYdotoolAsync(transcribedText);
                    break;
                default:
                    var topLevel = TopLevel.GetTopLevel(_lifetime.MainWindow);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(transcribedText);
                    }
                    else
                    {
                        throw new InvalidOperationException("Could not access clipboard");
                    }
                    break;
            }

            _appState.SetSuccess();
            _logger.Information("Text processed successfully: {Text}", transcribedText);
        }
        catch (Exception ex)
        {
            _appState.SetError(ex.Message);
            _logger.Error(ex, "Error during recording/transcription process");
        }
        finally
        {
            _appState.IsProcessing = false;
        }
    }

    private async Task CopyToClipboardWaylandAsync(string text)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wl-copy",
                    Arguments = $"\"{text.Replace("\"", "\\\"")}\"",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"wl-copy exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy to clipboard using wl-copy");
            throw;
        }
    }

    private async Task TypeWithYdotoolAsync(string text)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ydotool",
                    Arguments = $"type \"{text.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                throw new Exception($"ydotool exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to type text using ydotool");
            throw;
        }
    }

    private void ExitApplication()
    {
        _recordingService.Dispose();
        _transcriptionService.Dispose();
        _socketListener.Dispose();
        _lifetime.Shutdown();
    }

    private WindowIcon CreateTrayIcon(Color color)
    {
        const int size = 32;
        var bitmap = new WriteableBitmap(
            new PixelSize(size, size),
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Premul);

        using (var lockedBitmap = bitmap.Lock())
        {
            var backBuffer = lockedBitmap.Address;
            var stride = lockedBitmap.RowBytes;
            var pixelBytes = new byte[stride * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var dx = x - size / 2.0;
                    var dy = y - size / 2.0;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= size / 2.0)
                    {
                        var i = y * stride + x * 4;
                        pixelBytes[i] = color.B;     // Blue
                        pixelBytes[i + 1] = color.G; // Green
                        pixelBytes[i + 2] = color.R; // Red
                        pixelBytes[i + 3] = color.A; // Alpha
                    }
                }
            }

            Marshal.Copy(pixelBytes, 0, backBuffer, pixelBytes.Length);
        }

        return new WindowIcon(bitmap);
    }
}