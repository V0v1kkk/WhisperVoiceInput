using Akka.Actor;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Abstractions;

/// <summary>
/// Factory interface for creating actor Props.
/// This interface enables dependency injection and testing.
/// </summary>
public interface IActorPropsFactory
{
    /// <summary>
    /// Creates Props for AudioRecordingActor
    /// </summary>
    Props CreateAudioRecordingActorProps(AppSettings settings);

    /// <summary>
    /// Creates Props for TranscribingActor
    /// </summary>
    Props CreateTranscribingActorProps(AppSettings settings);

    /// <summary>
    /// Creates Props for PostProcessorActor.
    /// Returns null if post processing is disabled.
    /// </summary>
    Props? CreatePostProcessorActorProps(AppSettings settings);

    /// <summary>
    /// Creates Props for ResultSaverActor
    /// </summary>
    Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService);

    /// <summary>
    /// Creates Props for ObserverActor
    /// </summary>
    Props CreateObserverActorProps();
}