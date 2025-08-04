using Akka.Actor;
using Serilog;
using System;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Main orchestrator actor implementing FSM pattern.
/// Coordinates all transcription activities and manages state transitions.
/// </summary>
public class MainOrchestratorActor : FSM<AppState, StateData>, IWithStash
{
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

        // Handle actor termination events
        WhenUnhandled(evt =>
        {
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
            AudioRecordedEvent are => StartTranscription(are, evt.StateData),
            UpdateSettingsCommand => StashMessage(evt.StateData),
            Terminated terminated => HandleChildTerminated(terminated),
            _ => Stay()
        };
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
        return GoTo(AppState.Transcribing).Using(currentData);
    }

    private State<AppState, StateData> HandleTranscriptionCompleted(TranscriptionCompletedEvent evt, StateData currentData)
    {
        _logger.Information("Transcription completed");
            
        if (currentData.FrozenSettings.PostProcessingEnabled)
        {
            return StartPostProcessing(evt.Text, currentData);
        }
        else
        {
            return CompleteProcess(evt.Text, currentData);
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
        return CompleteProcess(evt.ProcessedText, currentData);
    }

    private State<AppState, StateData> CompleteProcess(string finalText, StateData currentData)
    {
        _logger.Information("Process completed successfully");
            
        // Send result for saving
        _resultSaverActor?.Tell(new ResultAvailableEvent(finalText));
            
        // Send success state manually (brief success notification)
        NotifyStateUpdate(AppState.Success);
            
        // Clean up child actors and unstash messages
        var newStateData = CleanupAndUnstash(currentData);
            
        // OnTransition will handle the Idle state notification
        return GoTo(AppState.Idle).Using(newStateData);
    }

    private State<AppState, StateData> UpdateSettings(UpdateSettingsCommand cmd, StateData currentData)
    {
        _logger.Information("Updating settings while idle");
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
        _logger.Error("Child actor {Actor} terminated unexpectedly", terminated.ActorRef);
            
        // Send error state manually (brief error notification)
        NotifyStateUpdate(AppState.Error, "Child actor terminated unexpectedly");
            
        // Clean up and unstash
        var errorStateData = CleanupAndUnstash(StateData);
            
        // OnTransition will handle the Idle state notification
        return GoTo(AppState.Idle).Using(errorStateData);
    }

    private void NotifyStateUpdate(AppState newState, string? errorMessage = null)
    {
        _observerActor.Tell(new StateUpdatedEvent(newState, errorMessage));
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "MainOrchestratorActor is restarting");
        base.PreRestart(reason, message);
    }
}

/// <summary>
/// State data containing only the frozen settings for the current transcription session
/// </summary>
public record StateData(AppSettings FrozenSettings);