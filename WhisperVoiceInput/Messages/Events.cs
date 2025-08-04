namespace WhisperVoiceInput.Messages
{
    // Base event marker interface
    public interface IEvent { }

    /// <summary>
    /// Application state enumeration
    /// </summary>
    public enum AppState
    {
        Idle,
        Recording,
        Transcribing,
        PostProcessing,
        Success,
        Error
    }

    #region AudioRecording Events

    /// <summary>
    /// Event indicating audio recording has completed
    /// </summary>
    public record AudioRecordedEvent(string FilePath) : IEvent;

    #endregion

    #region Transcribing Events

    /// <summary>
    /// Event indicating transcription has completed
    /// </summary>
    public record TranscriptionCompletedEvent(string Text) : IEvent;

    #endregion

    #region PostProcessing Events

    /// <summary>
    /// Event indicating post-processing has completed
    /// </summary>
    public record PostProcessedEvent(string ProcessedText) : IEvent;

    /// <summary>
    /// Event indicating post-processing has failed
    /// </summary>
    public record PostProcessingFailedEvent(string Error) : IEvent;

    #endregion

    #region Orchestrator Events

    /// <summary>
    /// Event indicating state has been updated
    /// </summary>
    public record StateUpdatedEvent(AppState State, string? ErrorMessage = null) : IEvent;

    /// <summary>
    /// Event indicating maximum retries have been exceeded
    /// </summary>
    public record MaxRetriesExceededEvent(Akka.Actor.IActorRef FailedActor) : IEvent;

    #endregion

    #region ResultSaver Events

    /// <summary>
    /// Event indicating result is available for saving
    /// </summary>
    public record ResultAvailableEvent(string Text) : IEvent;

    /// <summary>
    /// Event indicating result has been successfully saved
    /// </summary>
    public record ResultSavedEvent(string Text) : IEvent;

    #endregion

    #region Observer Events

    /// <summary>
    /// Event containing the observable result
    /// </summary>
    public record StateObservableResult(System.IObservable<StateUpdatedEvent> Observable) : IEvent;

    #endregion
}
