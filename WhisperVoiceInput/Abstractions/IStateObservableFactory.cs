using System;
using WhisperVoiceInput.Messages;

namespace WhisperVoiceInput.Abstractions;

/// <summary>
/// Interface for creating observable streams of application state
/// </summary>
public interface IStateObservableFactory
{
    /// <summary>
    /// Get an observable stream of state updates
    /// </summary>
    IObservable<StateUpdatedEvent> GetStateObservable();
}