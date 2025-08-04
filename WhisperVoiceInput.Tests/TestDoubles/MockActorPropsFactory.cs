using Akka.Actor;
using Akka.TestKit;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Tests.TestDoubles;

public class MockActorPropsFactory : IActorPropsFactory
{
    private readonly TimeSpan _recordingDelay;
    private readonly TimeSpan _transcriptionDelay;
    private readonly TimeSpan _postProcessingDelay;
    private readonly TimeSpan _savingDelay;
    private readonly IScheduler _scheduler;

    public MockActorPropsFactory(
        TimeSpan recordingDelay,
        TimeSpan transcriptionDelay,
        TimeSpan postProcessingDelay,
        TimeSpan savingDelay,
        IScheduler scheduler)
    {
        _recordingDelay = recordingDelay;
        _transcriptionDelay = transcriptionDelay;
        _postProcessingDelay = postProcessingDelay;
        _savingDelay = savingDelay;
        _scheduler = scheduler;
    }

    public Props CreateAudioRecordingActorProps(AppSettings settings)
        => MockAudioRecordingActor.Props(_recordingDelay, _scheduler)
            .WithDispatcher(CallingThreadDispatcher.Id);

    public Props CreateTranscribingActorProps(AppSettings settings)
        => MockTranscribingActor.Props(_transcriptionDelay, _scheduler)
            .WithDispatcher(CallingThreadDispatcher.Id);

    public Props? CreatePostProcessorActorProps(AppSettings settings)
        => settings.PostProcessingEnabled 
            ? MockPostProcessorActor.Props(_postProcessingDelay, _scheduler)
                .WithDispatcher(CallingThreadDispatcher.Id)
            : null;

    public Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService)
        => MockResultSaverActor.Props(_savingDelay, _scheduler)
            .WithDispatcher(CallingThreadDispatcher.Id);

    public Props CreateObserverActorProps()
        => Props.Create<ObserverActor>()
            .WithDispatcher(CallingThreadDispatcher.Id);
}