using Akka.Actor;
using Serilog;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Services;

/// <summary>
/// Production implementation of IActorPropsFactory that creates actors with properly typed loggers
/// </summary>
public class ActorPropsFactory : IActorPropsFactory
{
    private readonly ILogger _baseLogger;
    private readonly SoundFlowAudioService _audioService;

    public ActorPropsFactory(ILogger baseLogger, SoundFlowAudioService audioService)
    {
        _baseLogger = baseLogger;
        _audioService = audioService;
    }

    public Props CreateObserverActorProps()
    {
        var logger = _baseLogger.ForContext<ObserverActor>();
        return Props.Create(() => new ObserverActor(logger));
    }

    public Props CreateAudioRecordingActorProps(AppSettings settings)
    {
        var logger = _baseLogger.ForContext<AudioRecordingActor>();
        return Props.Create(() => new AudioRecordingActor(settings, logger, _audioService));
    }

    public Props CreateTranscribingActorProps(AppSettings settings)
    {
        var logger = _baseLogger.ForContext<TranscribingActor>();
        return Props.Create(() => new TranscribingActor(settings, logger));
    }

    public Props? CreatePostProcessorActorProps(AppSettings settings)
    {
        if (!settings.PostProcessingEnabled)
        {
            return null;
        }

        var logger = _baseLogger.ForContext<PostProcessorActor>();
        return Props.Create(() => new PostProcessorActor(settings, logger));
    }

    public Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService,
        IWaylandInputMethodClient waylandClient)
    {
        var logger = _baseLogger.ForContext<ResultSaverActor>();
        return Props.Create(() => new ResultSaverActor(settings, logger, clipboardService, waylandClient));
    }
}