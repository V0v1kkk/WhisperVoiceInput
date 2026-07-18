using Akka.Actor;
using Serilog;
using System;
using System.Reactive.Subjects;
using WhisperVoiceInput.Messages;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for managing state observation and providing observable state updates.
/// Handles the GetStateObservableCommand and provides StateObservableResult responses.
/// </summary>
public class ObserverActor : ReceiveActor
{
    private readonly ILogger _logger;
    private readonly BehaviorSubject<StateUpdatedEvent> _stateSubject;
    private readonly BehaviorSubject<ReprocessAvailableEvent> _reprocessSubject;

    public ObserverActor(ILogger logger)
    {
        _logger = logger;
        _stateSubject = new BehaviorSubject<StateUpdatedEvent>(new StateUpdatedEvent(AppState.Idle));
        _reprocessSubject = new BehaviorSubject<ReprocessAvailableEvent>(new ReprocessAvailableEvent(false));

        Receive<StateUpdatedEvent>(HandleStateUpdatedEvent);
        Receive<ReprocessAvailableEvent>(HandleReprocessAvailableEvent);
        Receive<GetStateObservableCommand>(HandleGetStateObservableCommand);
        Receive<GetReprocessObservableCommand>(HandleGetReprocessObservableCommand);
    }

    private void HandleStateUpdatedEvent(StateUpdatedEvent evt)
    {
        _logger.Debug("State updated to {State} with message: {Message}", evt.State, evt.ErrorMessage);
        _stateSubject.OnNext(evt);
    }

    private void HandleReprocessAvailableEvent(ReprocessAvailableEvent evt)
    {
        _logger.Debug("Reprocess availability changed: {IsAvailable}", evt.IsAvailable);
        _reprocessSubject.OnNext(evt);
    }

    private void HandleGetStateObservableCommand(GetStateObservableCommand cmd)
    {
        _logger.Debug("Providing state observable to requester");
        Sender.Tell(new StateObservableResult(_stateSubject));
    }

    private void HandleGetReprocessObservableCommand(GetReprocessObservableCommand cmd)
    {
        _logger.Debug("Providing reprocess observable to requester");
        Sender.Tell(new ReprocessObservableResult(_reprocessSubject));
    }

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "ObserverActor is restarting due to an exception");
        base.PreRestart(reason, message);
    }

    protected override void PostStop()
    {
        _stateSubject?.Dispose();
        _reprocessSubject?.Dispose();
        base.PostStop();
    }
}