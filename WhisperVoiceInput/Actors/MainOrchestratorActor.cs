using Akka.Actor;
using Akka.Pattern;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Helpers;
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

public record StateData(AppSettings FrozenSettings, Guid SessionId = default, string? LastOriginalText = null, string? LastAudioFilePath = null);

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
    private readonly IWaylandInputMethodClient _waylandClient;
    private readonly ILogger _logger;
    private readonly RetryPolicySettings _retrySettings;
    private readonly IActorRef _observerActor;

    // Child actor references (created only during active sessions)
    private IActorRef? _audioRecordingActor;
    private IActorRef? _transcribingActor;
    private IActorRef? _postProcessorActor;
    private IActorRef? _resultSaverActor;
    private Exception? _lastError;

    private Guid _currentSessionId;
    private string? _lastReprocessableAudioPath;

    public IStash Stash { get; set; } = null!;

    public MainOrchestratorActor(
        IActorPropsFactory propsFactory, 
        IClipboardService clipboardService,
        IWaylandInputMethodClient waylandClient,
        ILogger logger, 
        AppSettings initialSettings,
        RetryPolicySettings retrySettings,
        IActorRef observerActor)
    {
        _propsFactory = propsFactory;
        _clipboardService = clipboardService;
        _waylandClient = waylandClient;
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
                if (ex is DllNotFoundException)
                {
                    _logger.Error(ex, "Unrecoverable error - required native library not found");
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
            ReprocessCommand => HandleReprocess(evt.StateData),
            CancelPipelineCommand => Stay(),
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
            CancelPipelineCommand => HandleCancel(evt.StateData),
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
            TranscriptionCompletedEvent tce when IsCurrentSessionPipelineEvent(tce.SessionId, evt.StateData)
                => HandleTranscriptionCompleted(tce, evt.StateData),
            TranscriptionCompletedEvent => LogStaleEvent("TranscriptionCompletedEvent", evt.StateData),
            CancelPipelineCommand => HandleCancel(evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> HandlePostProcessingState(Event<StateData> evt)
    {
        return evt.FsmEvent switch
        {
            PostProcessedEvent ppe when IsCurrentSessionPipelineEvent(ppe.SessionId, evt.StateData)
                => HandlePostProcessingCompleted(ppe, evt.StateData),
            PostProcessedEvent => LogStaleEvent("PostProcessedEvent", evt.StateData),
            CancelPipelineCommand => HandleCancel(evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> StartRecording(StateData currentData)
    {
        _logger.Information("Starting recording session - freezing settings");

        _currentSessionId = Guid.CreateVersion7();

        // Clean up previous retained audio file
        if (_lastReprocessableAudioPath != null)
        {
            TryDeleteRetainedFile();
            NotifyReprocessAvailable(false);
        }

        // Create child actors with frozen settings
        CreateChildActors(currentData.FrozenSettings);
            
        _audioRecordingActor!.Tell(new RecordCommand());
            
        return GoTo(AppState.Recording).Using(currentData with { SessionId = _currentSessionId });
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
            ResultSavedEvent rse when IsCurrentSessionPipelineEvent(rse.SessionId, evt.StateData)
                => HandleSavingCompleted(rse, evt.StateData),
            ResultSavedEvent => LogStaleEvent("ResultSavedEvent", evt.StateData),
            CancelPipelineCommand => HandleCancel(evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
    }

    private State<AppState, StateData> StartSaving(string finalText, StateData currentData)
    {
        TryRunCompletionHook(finalText, currentData.FrozenSettings);
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

        RetainOrDeleteAudioFile(currentData, deleteIfNotRetained: false);

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
            CleanupAllChildActors();
        }

        // If KeepLastRecording was turned off, clean up retained file
        if (!cmd.Settings.KeepLastRecording && _lastReprocessableAudioPath != null)
        {
            TryDeleteRetainedFile();
            NotifyReprocessAvailable(false);
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
            CleanupAllChildActors();
        }

        _logger.Information("Creating child actors with frozen settings (session {SessionId})", _currentSessionId);

        _audioRecordingActor = Context.ActorOf(
            _propsFactory.CreateAudioRecordingActorProps(settings),
            $"audio-recording-{_currentSessionId}");
        Context.Watch(_audioRecordingActor);

        _transcribingActor = Context.ActorOf(
            _propsFactory.CreateTranscribingActorProps(settings),
            $"transcribing-{_currentSessionId}");
        Context.Watch(_transcribingActor);

        if (settings.PostProcessingEnabled)
        {
            var postProcessorProps = _propsFactory.CreatePostProcessorActorProps(settings);
            if (postProcessorProps != null)
            {
                _postProcessorActor = Context.ActorOf(postProcessorProps, $"post-processor-{_currentSessionId}");
                Context.Watch(_postProcessorActor);
            }
        }

        _resultSaverActor = Context.ActorOf(
            _propsFactory.CreateResultSaverActorProps(settings, _clipboardService, _waylandClient),
            $"result-saver-{_currentSessionId}");
        Context.Watch(_resultSaverActor);
    }

    private StateData CleanupAndUnstash(StateData currentData)
    {
        _logger.Information("Cleaning up child actors and unstashing messages");
            
        CleanupAllChildActors();

        // Get the current settings (might have been updated while processing)
        var currentSettings = currentData.FrozenSettings;
            
        // Unstash all messages to process any pending settings updates
        Stash.UnstashAll();
            
        return new StateData(currentSettings);
    }

    private void CleanupAllChildActors()
    {
        CleanupChildActor(ref _audioRecordingActor);
        CleanupChildActor(ref _transcribingActor);
        CleanupChildActor(ref _postProcessorActor);
        CleanupChildActor(ref _resultSaverActor);
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

        RetainOrDeleteAudioFile(StateData);

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

    private void TryRunCompletionHook(string resultText, AppSettings settings)
    {
        if (!settings.CompletionHookEnabled || string.IsNullOrWhiteSpace(settings.CompletionHookCommand))
            return;

        try
        {
            var resolvedCommand = ShellHelper.BuildHookCommand(settings.CompletionHookCommand, resultText);
            var (shell, flag) = ShellHelper.GetSystemShell();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                },
                EnableRaisingEvents = false
            };
            process.StartInfo.ArgumentList.Add(flag);
            process.StartInfo.ArgumentList.Add(resolvedCommand);

            process.Start();
            _logger.Information("Completion hook started: {Shell} {Flag} {Command}", shell, flag, settings.CompletionHookCommand);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start completion hook");
        }
    }

    private State<AppState, StateData> HandleCancel(StateData currentData)
    {
        _logger.Information("Pipeline cancelled by user in state {State}", StateName);

        _lastError = null;
        CleanupAllChildActors();

        RetainOrDeleteAudioFile(currentData);

        NotifyStateUpdate(AppState.Error, "Cancelled by user");

        Stash.UnstashAll();

        return GoTo(AppState.Idle).Using(new StateData(currentData.FrozenSettings));
    }

    private State<AppState, StateData> HandleReprocess(StateData currentData)
    {
        if (string.IsNullOrWhiteSpace(_lastReprocessableAudioPath))
        {
            _logger.Warning("Reprocess requested but no retained audio file path");
            NotifyReprocessAvailable(false);
            return Stay();
        }

        if (!File.Exists(_lastReprocessableAudioPath))
        {
            _logger.Warning("Reprocess requested but retained audio file no longer exists: {Path}", _lastReprocessableAudioPath);
            _lastReprocessableAudioPath = null;
            NotifyReprocessAvailable(false);
            return Stay();
        }

        _logger.Information("Starting reprocess of {Path}", _lastReprocessableAudioPath);

        _currentSessionId = Guid.CreateVersion7();
        var audioPath = _lastReprocessableAudioPath;

        // Freeze current settings for this reprocess session
        CreateChildActorsForReprocess(currentData.FrozenSettings);

        _transcribingActor!.Tell(new TranscribeCommand(audioPath));

        return GoTo(AppState.Transcribing).Using(
            currentData with { SessionId = _currentSessionId, LastAudioFilePath = audioPath });
    }

    private void CreateChildActorsForReprocess(AppSettings settings)
    {
        CleanupAllChildActors();

        _logger.Information("Creating child actors for reprocess (session {SessionId})", _currentSessionId);

        _transcribingActor = Context.ActorOf(
            _propsFactory.CreateTranscribingActorProps(settings),
            $"transcribing-{_currentSessionId}");
        Context.Watch(_transcribingActor);

        if (settings.PostProcessingEnabled)
        {
            var postProcessorProps = _propsFactory.CreatePostProcessorActorProps(settings);
            if (postProcessorProps != null)
            {
                _postProcessorActor = Context.ActorOf(postProcessorProps, $"post-processor-{_currentSessionId}");
                Context.Watch(_postProcessorActor);
            }
        }

        _resultSaverActor = Context.ActorOf(
            _propsFactory.CreateResultSaverActorProps(settings, _clipboardService, _waylandClient),
            $"result-saver-{_currentSessionId}");
        Context.Watch(_resultSaverActor);
    }

    private bool IsCurrentSessionPipelineEvent(Guid eventSessionId, StateData stateData)
    {
        if (eventSessionId != default)
            return eventSessionId == stateData.SessionId;

        return stateData.SessionId == _currentSessionId;
    }

    private State<AppState, StateData> LogStaleEvent(string eventName, StateData currentData)
    {
        _logger.Warning("Ignoring stale {Event} from session {EventSession} (current session: {CurrentSession})",
            eventName, currentData.SessionId, _currentSessionId);
        return Stay();
    }

    private void RetainOrDeleteAudioFile(StateData data, bool deleteIfNotRetained = true)
    {
        if (data.FrozenSettings.KeepLastRecording
            && !string.IsNullOrWhiteSpace(data.LastAudioFilePath))
        {
            _lastReprocessableAudioPath = data.LastAudioFilePath;
            NotifyReprocessAvailable(true);
        }
        else
        {
            if (deleteIfNotRetained)
                TryDeleteFileIfAny(data);
            NotifyReprocessAvailable(false);
        }
    }

    private void NotifyReprocessAvailable(bool available)
    {
        _observerActor.Tell(new ReprocessAvailableEvent(available));
    }

    private void TryDeleteRetainedFile()
    {
        if (_lastReprocessableAudioPath == null) return;
        try
        {
            if (File.Exists(_lastReprocessableAudioPath))
            {
                File.Delete(_lastReprocessableAudioPath);
                _logger.Information("Deleted retained audio file: {FilePath}", _lastReprocessableAudioPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to delete retained audio file: {FilePath}", _lastReprocessableAudioPath);
        }
        _lastReprocessableAudioPath = null;
    }

    protected override void PostStop()
    {
        TryDeleteRetainedFile();
        base.PostStop();
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "MainOrchestratorActor is restarting");
        base.PreRestart(reason, message);
    }
}
