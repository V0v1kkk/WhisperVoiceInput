using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WhisperVoiceInput.Models;

public enum TrayIconState
{
    Idle,
    Recording,
    Processing,
    Success,
    Error
}

public partial class AppState : ObservableObject
{
    [ObservableProperty]
    private TrayIconState _trayIconState;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isProcessing;

    private DateTime? _lastStateChange;

    partial void OnTrayIconStateChanged(TrayIconState value)
    {
        _lastStateChange = DateTime.Now;
            
        // Reset error message when changing state
        if (value != TrayIconState.Error)
        {
            ErrorMessage = string.Empty;
        }
    }

    partial void OnIsRecordingChanged(bool value)
    {
        TrayIconState = value ? TrayIconState.Recording : TrayIconState.Idle;
    }

    partial void OnIsProcessingChanged(bool value)
    {
        if (value)
        {
            TrayIconState = TrayIconState.Processing;
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