namespace WhisperVoiceInput.Abstractions;

/// <summary>
/// Interface for toggling recording state
/// </summary>
public interface IRecordingToggler
{
    /// <summary>
    /// Toggle recording on/off
    /// </summary>
    void ToggleRecording();
}