using System;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive.Linq;

namespace WhisperVoiceInput.Models;

public enum TrayIconState
{
    Idle,
    Recording,
    Processing,
    Success,
    Error
}

public class AppState : ReactiveObject
{
    private TrayIconState _trayIconState;
    private string _errorMessage = string.Empty;
    private bool _isRecording;
    private bool _isProcessing;
    private DateTime? _lastStateChange;

    public AppState()
    {
        // Handle state changes based on recording/processing flags
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
    }

    public TrayIconState TrayIconState
    {
        get => _trayIconState;
        set => this.RaiseAndSetIfChanged(ref _trayIconState, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => this.RaiseAndSetIfChanged(ref _isRecording, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
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

    public Color GetTrayIconColor()
    {
        return TrayIconState switch
        {
            TrayIconState.Idle => Colors.White,
            TrayIconState.Recording => Colors.Yellow,
            TrayIconState.Processing => Colors.LightBlue,
            TrayIconState.Success => Colors.Green,
            TrayIconState.Error => Colors.Red,
            _ => Colors.White
        };
    }

    public bool ShouldRevertToIdle()
    {
        if (_lastStateChange == null || 
            (TrayIconState != TrayIconState.Success && TrayIconState != TrayIconState.Error))
        {
            return false;
        }

        return (DateTime.Now - _lastStateChange.Value).TotalSeconds >= 5;
    }
}