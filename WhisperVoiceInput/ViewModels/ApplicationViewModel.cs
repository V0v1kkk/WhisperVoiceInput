using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.ViewModels;

public partial class ApplicationViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger _logger;
    private readonly AppState _appState;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly AudioRecordingService _recordingService;
    private readonly TranscriptionService _transcriptionService;
    private readonly CommandSocketListener _socketListener;
    private readonly AppSettings _settings;
    private WindowIcon? _currentIcon;
    private bool _mainWindowIsVisible;
    private bool _isDisposed;

    [ObservableProperty]
    private string _tooltipText = "WhisperVoiceInput";

    public ApplicationViewModel(IClassicDesktopStyleApplicationLifetime lifetime, ILogger logger, AppSettings settings)
    {
        _lifetime = lifetime;
        _logger = logger;
        _settings = settings;
        _appState = new AppState();

        // Initialize services
        _recordingService = new AudioRecordingService(logger);
        _transcriptionService = new TranscriptionService(logger, settings);

        var socketPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperVoiceInput",
            "command.sock");

        _socketListener = new CommandSocketListener(
            logger,
            socketPath,
            async () => await StartRecordingAsync());

        // Start socket listener
        _socketListener.Start();

        // Set up state change handling
        _appState.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppState.TrayIconState) ||
                args.PropertyName == nameof(AppState.ErrorMessage))
            {
                UpdateTrayIcon();
            }
        };

        // Create initial icon
        UpdateTrayIcon();

        // Start timer to check for state reversion
        StartStateCheckTimer();
    }

    public WindowIcon Icon => _currentIcon ??= CreateTrayIcon(_appState.GetTrayIconColor());

    public bool MainWindowIsVisible
    {
        get => _mainWindowIsVisible;
        set
        {
            if (_mainWindowIsVisible != value)
            {
                _mainWindowIsVisible = value;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void SwitchWindowShownState()
    {
        MainWindowIsVisible = !MainWindowIsVisible;
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (_appState.IsRecording || _appState.IsProcessing)
            return;

        string? audioFilePath = null;

        try
        {
            _appState.IsRecording = true;
            audioFilePath = await _recordingService.StartRecordingAsync();

            _appState.IsProcessing = true;
            var transcribedText = await _transcriptionService.TranscribeAudioAsync(audioFilePath);

            if (_settings.UseWlCopy)
            {
                await CopyToClipboardWaylandAsync(transcribedText);
            }
            else
            {
                var topLevel = TopLevel.GetTopLevel(_lifetime.MainWindow);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(transcribedText);
                }
                else
                {
                    throw new InvalidOperationException("Could not access clipboard");
                }
            }

            _appState.SetSuccess();
            _logger.Information("Text copied to clipboard: {Text}", transcribedText);
        }
        catch (Exception ex)
        {
            _appState.SetError(ex.Message);
            _logger.Error(ex, "Error during recording/transcription process");
        }
        finally
        {
            _appState.IsRecording = false;
            _appState.IsProcessing = false;
        }
    }

    private async Task CopyToClipboardWaylandAsync(string text)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
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

    [RelayCommand]
    private void ExitApplication()
    {
        _lifetime.Shutdown();
    }

    private void UpdateTrayIcon()
    {
        var color = _appState.GetTrayIconColor();
        _currentIcon = CreateTrayIcon(color);
        OnPropertyChanged(nameof(Icon));

        TooltipText = _appState.TrayIconState switch
        {
            TrayIconState.Error => $"Error: {_appState.ErrorMessage}",
            TrayIconState.Recording => "Recording in progress...",
            TrayIconState.Processing => "Processing audio...",
            TrayIconState.Success => "Transcription copied to clipboard!",
            _ => "WhisperVoiceInput"
        };
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

    private void StartStateCheckTimer()
    {
        Task.Run(async () =>
        {
            while (!_isDisposed)
            {
                await Task.Delay(1000);
                if (_appState.ShouldRevertToIdle())
                {
                    _appState.TrayIconState = TrayIconState.Idle;
                }
            }
        });
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _recordingService.Dispose();
            _transcriptionService.Dispose();
            _socketListener.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}