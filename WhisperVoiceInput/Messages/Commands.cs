using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Messages
{
    // Base command marker interface
    public interface ICommand { }

    #region Orchestrator Commands

    /// <summary>
    /// Command to toggle recording on/off
    /// </summary>
    public record ToggleCommand() : ICommand;

    /// <summary>
    /// Command to update settings
    /// </summary>
    public record UpdateSettingsCommand(AppSettings Settings) : ICommand;

    /// <summary>
    /// Command to retry after failure
    /// </summary>
    public record RetryCommand() : ICommand;

    #endregion

    #region AudioRecording Commands

    /// <summary>
    /// Command to start recording
    /// </summary>
    public record RecordCommand() : ICommand;

    /// <summary>
    /// Command to stop recording
    /// </summary>
    public record StopRecordingCommand() : ICommand;

    /// <summary>
    /// Internal self-message signaling a recording timeout within AudioRecordingActor
    /// </summary>
    public record RecordingTimeout() : ICommand;

    #endregion

    #region Transcribing Commands

    /// <summary>
    /// Command to transcribe audio file
    /// </summary>
    public record TranscribeCommand(string AudioFile) : ICommand;

    /// <summary>
    /// Internal self-message signaling a transcription timeout within TranscribingActor
    /// </summary>
    public record TranscriptionTimeout() : ICommand;

    #endregion

    #region PostProcessing Commands

    /// <summary>
    /// Command to post-process transcribed text
    /// </summary>
    public record PostProcessCommand(string Text) : ICommand;

    /// <summary>
    /// Internal self-message signaling a post-processing timeout within PostProcessorActor
    /// </summary>
    public record PostProcessingTimeout() : ICommand;

    #endregion

    #region Observer Commands

    /// <summary>
    /// Command to get the state observable from observer actor
    /// </summary>
    public record GetStateObservableCommand : ICommand;

    #endregion

    #region Socket Listener Commands

    /// <summary>
    /// Command to start listening for socket connections
    /// </summary>
    public record StartListeningCommand : ICommand;

    /// <summary>
    /// Command to stop listening for socket connections
    /// </summary>
    public record StopListeningCommand : ICommand;

    #endregion
}
