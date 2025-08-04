using Akka.Actor;
using Akka.TestKit;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Tests.TestDoubles;

public class MixedActorPropsFactory : IActorPropsFactory
{
    private readonly TimeSpan _recordingDelay;
    private readonly TimeSpan _transcriptionDelay;
    private readonly TimeSpan _postProcessingDelay;
    private readonly TimeSpan _savingDelay;
    private readonly IScheduler _scheduler;
        
    private readonly bool _failAudioRecording;
    private readonly bool _failTranscribing;
    private readonly bool _failPostProcessor;
    private readonly bool _failResultSaver;

    public MixedActorPropsFactory(
        TimeSpan recordingDelay,
        TimeSpan transcriptionDelay,
        TimeSpan postProcessingDelay,
        TimeSpan savingDelay,
        IScheduler scheduler,
        bool failAudioRecording = false,
        bool failTranscribing = false,
        bool failPostProcessor = false,
        bool failResultSaver = false)
    {
        _recordingDelay = recordingDelay;
        _transcriptionDelay = transcriptionDelay;
        _postProcessingDelay = postProcessingDelay;
        _savingDelay = savingDelay;
        _scheduler = scheduler;
        _failAudioRecording = failAudioRecording;
        _failTranscribing = failTranscribing;
        _failPostProcessor = failPostProcessor;
        _failResultSaver = failResultSaver;
    }

    public Props CreateAudioRecordingActorProps(AppSettings settings)
        => _failAudioRecording 
            ? FailingAudioRecordingActor.Props().WithDispatcher(CallingThreadDispatcher.Id)
            : MockAudioRecordingActor.Props(_recordingDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);

    public Props CreateTranscribingActorProps(AppSettings settings)
        => _failTranscribing 
            ? FailingTranscribingActor.Props().WithDispatcher(CallingThreadDispatcher.Id)
            : MockTranscribingActor.Props(_transcriptionDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);

    public Props? CreatePostProcessorActorProps(AppSettings settings)
    {
        if (!settings.PostProcessingEnabled)
            return null;
                
        return _failPostProcessor 
            ? FailingPostProcessorActor.Props().WithDispatcher(CallingThreadDispatcher.Id)
            : MockPostProcessorActor.Props(_postProcessingDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);
    }

    public Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService)
        => _failResultSaver 
            ? FailingResultSaverActor.Props().WithDispatcher(CallingThreadDispatcher.Id)
            : MockResultSaverActor.Props(_savingDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);

    public Props CreateObserverActorProps()
        => Props.Create<ObserverActor>().WithDispatcher(CallingThreadDispatcher.Id);
}