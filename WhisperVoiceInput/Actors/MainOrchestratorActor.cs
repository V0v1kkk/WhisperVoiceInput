using Akka.Actor;
using Akka.Pattern;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Actors;

public enum AppState
{
    Idle,
    Recording,
    Transcribing,
    PostProcessing,
    Saving,
    Success,
    Error
}

public record StateData(AppSettings FrozenSettings, string? LastOriginalText = null, string? LastAudioFilePath = null);

public class MainOrchestratorActor : FSM<AppState, StateData>, IWithStash
{
    public static class StepNames
    {
        public const string AudioRecording = "Audio Recording";
        public const string Transcribing = "Transcribing";
        public const string PostProcessing = "Post-Processing";
        public const string ResultSaving = "Result Saving";
    }

    // Internal messages for dataset appending results
    private sealed record DatasetAppendSucceeded(string Path) : IEvent;
    private sealed record DatasetAppendFailed(Exception Exception, string Path) : IEvent;

    private readonly IActorPropsFactory _propsFactory;
    private readonly IClipboardService _clipboardService;
    private readonly ILogger _logger;
    private readonly RetryPolicySettings _retrySettings;
    private readonly IActorRef _observerActor;

    // Child actor references (created only during active sessions)
    private IActorRef? _audioRecordingActor;
    private IActorRef? _transcribingActor;
    private IActorRef? _postProcessorActor;
    private IActorRef? _resultSaverActor;
    private Exception? _lastError;

    public IStash Stash { get; set; } = null!;

    public MainOrchestratorActor(
        IActorPropsFactory propsFactory, 
        IClipboardService clipboardService,
        ILogger logger, 
        AppSettings initialSettings,
        RetryPolicySettings retrySettings,
        IActorRef observerActor)
    {
        _propsFactory = propsFactory;
        _clipboardService = clipboardService;
        _logger = logger;
        _retrySettings = retrySettings;
        _observerActor = observerActor;

        InitializeFSM(initialSettings);
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: _retrySettings.MaxRetries,
            withinTimeRange: _retrySettings.RetryTimeWindow,
            localOnlyDecider: ex =>
            {
                _lastError = ex;
                
                if (ex is FileNotFoundException)
                {
                    _logger.Error(ex, "Unrecoverable error");
                    return Directive.Stop;
                }
                if (ex is UserConfiguredTimeoutException)
                {
                    _logger.Error(ex, "User-configured timeout is unrecoverable");
                    return Directive.Stop;
                }
                
                _logger.Error(ex, "Child actor failed, will restart or stop based on retry count");
                return Directive.Restart; // OneForOneStrategy will automatically stop after maxNrOfRetries
            });
    }

    private void InitializeFSM(AppSettings initialSettings)
    {
        var initialStateData = new StateData(initialSettings);
        StartWith(AppState.Idle, initialStateData);

        // Define state handlers
        When(AppState.Idle, HandleIdleState);
        When(AppState.Recording, HandleRecordingState);
        When(AppState.Transcribing, HandleTranscribingState);
        When(AppState.PostProcessing, HandlePostProcessingState);
        When(AppState.Saving, HandleSavingState);
        
        WhenUnhandled(evt =>
        {
            switch (evt.FsmEvent)
            {
                case DatasetAppendSucceeded s:
                    _logger.Debug("Dataset entry appended to {Path}", s.Path);
                    return Stay().Using(evt.StateData);
                case DatasetAppendFailed f:
                    _logger.Error(f.Exception, "Failed to append dataset entry to {Path}", f.Path);
                    return Stay().Using(evt.StateData);
            }
            _logger.Warning("Unhandled event in state {State}: {Event}", StateName, evt.FsmEvent);
            return Stay();
        });

        OnTransition((fromState, toState) =>
        {
            _logger.Information("State transition: {From} -> {To}", fromState, toState);
            NotifyStateUpdate(toState);
        });

        Initialize();
    }

    private State<AppState, StateData> HandleIdleState(Event<StateData> evt)
    {
        return evt.FsmEvent switch
        {
            ToggleCommand => StartRecording(evt.StateData),
            UpdateSettingsCommand cmd => UpdateSettings(cmd, evt.StateData),
            _ => Stay()
        };
    }

    private State<AppState, StateData> HandleRecordingState(Event<StateData> evt)
    {
        return evt.FsmEvent switch
        {
            ToggleCommand => StopRecording(evt.StateData),
            RecordingStartedEvent rse => HandleRecordingStarted(rse, evt.StateData),
            AudioRecordedEvent are => StartTranscription(are, evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> HandleRecordingStarted(RecordingStartedEvent evt, StateData currentData)
    {
        // Track temp file path early (in case recording later fails or times out)
        var updated = currentData with { LastAudioFilePath = evt.FilePath };
        return Stay().Using(updated);
    }

    private State<AppState, StateData> HandleTranscribingState(Event<StateData> evt)
    {
        return evt.FsmEvent switch
        {
            TranscriptionCompletedEvent tce => HandleTranscriptionCompleted(tce, evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> HandlePostProcessingState(Event<StateData> evt)
    {
        return evt.FsmEvent switch
        {
            PostProcessedEvent ppe => HandlePostProcessingCompleted(ppe, evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> StartRecording(StateData currentData)
    {
        _logger.Information("Starting recording session - freezing settings");
            
        // Create child actors with frozen settings
        CreateChildActors(currentData.FrozenSettings);
            
        _audioRecordingActor!.Tell(new RecordCommand());
            
        return GoTo(AppState.Recording).Using(currentData);
    }

    private State<AppState, StateData> StopRecording(StateData currentData)
    {
        _logger.Information("Stopping recording");
        _audioRecordingActor?.Tell(new StopRecordingCommand());
        return Stay().Using(currentData);
    }

    private State<AppState, StateData> StartTranscription(AudioRecordedEvent evt, StateData currentData)
    {
        _logger.Information("Starting transcription");
        _transcribingActor!.Tell(new TranscribeCommand(evt.FilePath));
        // Track current session audio file for possible cleanup on failure
        var updated = currentData with { LastAudioFilePath = evt.FilePath };
        return GoTo(AppState.Transcribing).Using(updated);
    }

    private State<AppState, StateData> HandleTranscriptionCompleted(TranscriptionCompletedEvent evt, StateData currentData)
    {
        _logger.Information("Transcription completed");
        
        // Only dataset saving when Post-Processing is enabled; otherwise skip dataset saving entirely
        if (currentData.FrozenSettings.PostProcessingEnabled)
        {
            // Preserve original transcription for dataset saving after post-processing
            var updated = currentData with { LastOriginalText = evt.Text };
            return StartPostProcessing(evt.Text, updated);
        }
        else
        {
            // No dataset saving when post-processing is disabled
            return StartSaving(evt.Text, currentData);
        }
    }

    private State<AppState, StateData> StartPostProcessing(string text, StateData currentData)
    {
        _logger.Information("Starting post-processing");
        _postProcessorActor?.Tell(new PostProcessCommand(text));
        return GoTo(AppState.PostProcessing).Using(currentData);
    }

    private State<AppState, StateData> HandlePostProcessingCompleted(PostProcessedEvent evt, StateData currentData)
    {
        _logger.Information("Post-processing completed");
        
        // If dataset saving is enabled (and we are post-processing), append pair (original, processed)
        if (currentData.FrozenSettings.DatasetSavingEnabled &&
            !string.IsNullOrWhiteSpace(currentData.FrozenSettings.DatasetFilePath))
        {
            var original = currentData.LastOriginalText ?? evt.ProcessedText;
            TryAppendDatasetAsync(original, evt.ProcessedText, currentData.FrozenSettings.DatasetFilePath);
        }
        
        return StartSaving(evt.ProcessedText, currentData);
    }

    private State<AppState, StateData> HandleSavingState(Event<StateData> evt)
    {
        return evt.FsmEvent switch
        {
            ResultSavedEvent rse => HandleSavingCompleted(rse, evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> StartSaving(string finalText, StateData currentData)
    {
        _logger.Information("Starting result saving");
        _resultSaverActor?.Tell(new ResultAvailableEvent(finalText));
        return GoTo(AppState.Saving).Using(currentData);
    }

    private State<AppState, StateData> HandleSavingCompleted(ResultSavedEvent evt, StateData currentData)
    {
        _logger.Information("Result saving completed successfully");
        return CompleteProcess(currentData);
    }

    private State<AppState, StateData> CompleteProcess(StateData currentData)
    {
        _logger.Information("Process completed successfully");
            
        // Send success state manually (brief success notification)
        NotifyStateUpdate(AppState.Success);
            
        // Unstash any pending messages (e.g., settings updates). Keep child actors alive for reuse.
        Stash.UnstashAll();
            
        // OnTransition will handle the Idle state notification
        return GoTo(AppState.Idle).Using(currentData);
    }

    private State<AppState, StateData> UpdateSettings(UpdateSettingsCommand cmd, StateData currentData)
    {
        _logger.Information("Updating settings while idle");
        // If settings changed compared to the last frozen settings, stop cached actors
        if (!currentData.FrozenSettings.Equals(cmd.Settings))
        {
            _logger.Information("Settings changed; stopping cached child actors to rebuild on next cycle");
            CleanupChildActor(ref _audioRecordingActor);
            CleanupChildActor(ref _transcribingActor);
            CleanupChildActor(ref _postProcessorActor);
            CleanupChildActor(ref _resultSaverActor);
        }

        var newStateData = new StateData(cmd.Settings);
        return Stay().Using(newStateData);
    }

    private State<AppState, StateData> StashMessage(StateData currentData)
    {
        _logger.Information("Stashing settings update during active session");
        Stash.Stash();
        return Stay().Using(currentData);
    }

    private void CreateChildActors(AppSettings settings)
    {
        // Reuse existing child actors if they are already present and structurally match current settings
        var postProcessorRequired = settings.PostProcessingEnabled;
        var haveAllRequired = _audioRecordingActor != null
                              && _transcribingActor != null
                              && _resultSaverActor != null
                              && (!postProcessorRequired || _postProcessorActor != null);
        var haveAny = _audioRecordingActor != null || _transcribingActor != null || _postProcessorActor != null || _resultSaverActor != null;

        if (haveAllRequired)
        {
            _logger.Information("Reusing existing child actors with unchanged settings");
            return;
        }
        else if (haveAny)
        {
            _logger.Information("Existing child actors do not match required set; rebuilding");
            CleanupChildActor(ref _audioRecordingActor);
            CleanupChildActor(ref _transcribingActor);
            CleanupChildActor(ref _postProcessorActor);
            CleanupChildActor(ref _resultSaverActor);
        }

        _logger.Information("Creating child actors with frozen settings");

        _audioRecordingActor = Context.ActorOf(
            _propsFactory.CreateAudioRecordingActorProps(settings),
            "audio-recording");
        Context.Watch(_audioRecordingActor);

        _transcribingActor = Context.ActorOf(
            _propsFactory.CreateTranscribingActorProps(settings),
            "transcribing");
        Context.Watch(_transcribingActor);

        if (settings.PostProcessingEnabled)
        {
            var postProcessorProps = _propsFactory.CreatePostProcessorActorProps(settings);
            if (postProcessorProps != null)
            {
                _postProcessorActor = Context.ActorOf(postProcessorProps, "post-processor");
                Context.Watch(_postProcessorActor);
            }
        }

        _resultSaverActor = Context.ActorOf(
            _propsFactory.CreateResultSaverActorProps(settings, _clipboardService),
            "result-saver");
        Context.Watch(_resultSaverActor);
    }

    private StateData CleanupAndUnstash(StateData currentData)
    {
        _logger.Information("Cleaning up child actors and unstashing messages");
            
        // Stop and unwatch child actors
        CleanupChildActor(ref _audioRecordingActor);
        CleanupChildActor(ref _transcribingActor);
        CleanupChildActor(ref _postProcessorActor);
        CleanupChildActor(ref _resultSaverActor);

        // Get the current settings (might have been updated while processing)
        var currentSettings = currentData.FrozenSettings;
            
        // Unstash all messages to process any pending settings updates
        Stash.UnstashAll();
            
        return new StateData(currentSettings);
    }

    private void CleanupChildActor(ref IActorRef? actorRef)
    {
        if (actorRef != null)
        {
            Context.Unwatch(actorRef);
            Context.Stop(actorRef);
            actorRef = null;
        }
    }

    private State<AppState, StateData> HandleChildTerminated(Terminated terminated)
    {
        _logger.Error(_lastError, "Child actor {Actor} terminated unexpectedly", terminated.ActorRef);
        
        var stepName = terminated.ActorRef switch 
        {
            var a when a.Equals(_audioRecordingActor) => StepNames.AudioRecording,
            var a when a.Equals(_transcribingActor) => StepNames.Transcribing,
            var a when a.Equals(_postProcessorActor) => StepNames.PostProcessing,
            var a when a.Equals(_resultSaverActor) => StepNames.ResultSaving,
            _ => terminated.ActorRef.Path.Name
        };
        
        var errorMessage = _lastError != null 
            ? $"Error on {stepName} step: {_lastError.Message}"
            : $"Unexpected error on {stepName} step"; 
            
        // Send error state manually (brief error notification)
        NotifyStateUpdate(AppState.Error, errorMessage);
            
        // Clean up resources that will no longer be used
        TryDeleteFileIfAny(StateData);
        // Clean up and unstash
        var errorStateData = CleanupAndUnstash(StateData with { LastAudioFilePath = null, LastOriginalText = null });
            
        // OnTransition will handle the Idle state notification
        return GoTo(AppState.Idle).Using(errorStateData);
    }

    private void TryDeleteFileIfAny(StateData data)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(data.LastAudioFilePath) && File.Exists(data.LastAudioFilePath))
            {
                File.Delete(data.LastAudioFilePath);
                _logger.Information("Deleted audio file after failure/timeout: {FilePath}", data.LastAudioFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to delete audio file after failure/timeout: {FilePath}", data.LastAudioFilePath);
        }
    }

    private void NotifyStateUpdate(AppState newState, string? errorMessage = null)
    {
        _observerActor.Tell(new StateUpdatedEvent(newState, errorMessage));
    }

    private void TryAppendDatasetAsync(string original, string processed, string path)
    {
        try
        {
            var entry = $"{original}{Environment.NewLine}-{Environment.NewLine}{processed}{Environment.NewLine}---{Environment.NewLine}";
            var self = Self; // capture Self for async continuation
            File.AppendAllTextAsync(path, entry)
                .ContinueWith<object>(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                        return new DatasetAppendFailed(t.Exception.GetBaseException(), path);
                    return new DatasetAppendSucceeded(path);
                }, TaskScheduler.Default)
                .PipeTo(self);
        }
        catch (Exception ex)
        {
            Self.Tell(new DatasetAppendFailed(ex, path));
        }
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "MainOrchestratorActor is restarting");
        base.PreRestart(reason, message);
    }
}
