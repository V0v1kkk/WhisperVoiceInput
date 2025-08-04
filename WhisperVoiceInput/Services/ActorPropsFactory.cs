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
        
    public ActorPropsFactory(ILogger baseLogger)
    {
        _baseLogger = baseLogger;
    }

    public Props CreateObserverActorProps()
    {
        var logger = _baseLogger.ForContext<ObserverActor>();
        return Props.Create(() => new ObserverActor(logger));
    }

    public Props CreateAudioRecordingActorProps(AppSettings settings)
    {
        var logger = _baseLogger.ForContext<AudioRecordingActor>();
        return Props.Create(() => new AudioRecordingActor(settings, logger));
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

    public Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService)
    {
        var logger = _baseLogger.ForContext<ResultSaverActor>();
        return Props.Create(() => new ResultSaverActor(settings, logger, clipboardService));
    }
}