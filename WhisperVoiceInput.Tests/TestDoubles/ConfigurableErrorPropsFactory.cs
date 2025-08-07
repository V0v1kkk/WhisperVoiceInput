using System;
using Akka.Actor;
using Akka.TestKit;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Messages;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Configurable factory that allows injecting error behavior from test methods.
/// Can be used both with lambda functions for specific exceptions and boolean flags
/// for simple error scenarios. Replaces multiple specialized factories.
/// </summary>
public class ConfigurableErrorPropsFactory : IActorPropsFactory
{
    private readonly TimeSpan _recordingDelay;
    private readonly TimeSpan _transcriptionDelay;
    private readonly TimeSpan _postProcessingDelay;
    private readonly TimeSpan _savingDelay;
    private readonly IScheduler _scheduler;
    
    // Error providers for specific exceptions
    private Func<Exception>? _transcribingErrorProvider;
    private Func<Exception>? _audioRecordingErrorProvider;
    private Func<Exception>? _postProcessingErrorProvider;
    private Func<Exception>? _resultSaverErrorProvider;
    
    // Boolean flags for backwards compatibility with MixedActorPropsFactory
    private bool _failAudioRecording;
    private bool _failTranscribing;
    private bool _failPostProcessor;
    private bool _failResultSaver;

    public ConfigurableErrorPropsFactory(
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

    // Fluent API for specific exception configuration  
    public ConfigurableErrorPropsFactory WithTranscribingActorThatThrows(Func<Exception> errorProvider)
    {
        _transcribingErrorProvider = errorProvider;
        return this;
    }

    public ConfigurableErrorPropsFactory WithAudioRecordingActorThatThrows(Func<Exception> errorProvider)
    {
        _audioRecordingErrorProvider = errorProvider;
        return this;
    }

    public ConfigurableErrorPropsFactory WithPostProcessingActorThatThrows(Func<Exception> errorProvider)
    {
        _postProcessingErrorProvider = errorProvider;
        return this;
    }

    public ConfigurableErrorPropsFactory WithResultSaverActorThatThrows(Func<Exception> errorProvider)
    {
        _resultSaverErrorProvider = errorProvider;
        return this;
    }
    
    // Simple boolean API for backwards compatibility with MixedActorPropsFactory
    public ConfigurableErrorPropsFactory WithFailingAudioRecording(bool fail = true)
    {
        _failAudioRecording = fail;
        return this;
    }

    public ConfigurableErrorPropsFactory WithFailingTranscribing(bool fail = true)
    {
        _failTranscribing = fail;
        return this;
    }

    public ConfigurableErrorPropsFactory WithFailingPostProcessor(bool fail = true)
    {
        _failPostProcessor = fail;
        return this;
    }

    public ConfigurableErrorPropsFactory WithFailingResultSaver(bool fail = true)
    {
        _failResultSaver = fail;
        return this;
    }

    // Factory methods
    public Props CreateAudioRecordingActorProps(AppSettings settings)
    {
        if (_audioRecordingErrorProvider != null)
            return ConfigurableErrorAudioRecordingActor.Props(_audioRecordingErrorProvider).WithDispatcher(CallingThreadDispatcher.Id);
        
        if (_failAudioRecording)
            return FailingAudioRecordingActor.Props().WithDispatcher(CallingThreadDispatcher.Id);
        
        return MockAudioRecordingActor.Props(_recordingDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);
    }

    public Props CreateTranscribingActorProps(AppSettings settings)
    {
        if (_transcribingErrorProvider != null)
            return ConfigurableErrorTranscribingActor.Props(_transcribingErrorProvider).WithDispatcher(CallingThreadDispatcher.Id);
        
        if (_failTranscribing)
            return FailingTranscribingActor.Props().WithDispatcher(CallingThreadDispatcher.Id);
        
        return MockTranscribingActor.Props(_transcriptionDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);
    }

    public Props? CreatePostProcessorActorProps(AppSettings settings)
    {
        if (!settings.PostProcessingEnabled) return null;
        
        if (_postProcessingErrorProvider != null)
            return ConfigurableErrorPostProcessorActor.Props(_postProcessingErrorProvider).WithDispatcher(CallingThreadDispatcher.Id);
        
        if (_failPostProcessor)
            return FailingPostProcessorActor.Props().WithDispatcher(CallingThreadDispatcher.Id);
        
        return MockPostProcessorActor.Props(_postProcessingDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);
    }

    public Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService)
    {
        if (_resultSaverErrorProvider != null)
            return ConfigurableErrorResultSaverActor.Props(_resultSaverErrorProvider).WithDispatcher(CallingThreadDispatcher.Id);
        
        if (_failResultSaver)
            return FailingResultSaverActor.Props().WithDispatcher(CallingThreadDispatcher.Id);
        
        return MockResultSaverActor.Props(_savingDelay, _scheduler).WithDispatcher(CallingThreadDispatcher.Id);
    }

    public Props CreateObserverActorProps()
        => Props.Create<ObserverActor>().WithDispatcher(CallingThreadDispatcher.Id);
}

// Configurable error actors for injection of specific exceptions
public class ConfigurableErrorAudioRecordingActor : ReceiveActor
{
    public ConfigurableErrorAudioRecordingActor(Func<Exception> errorProvider)
    {
        Receive<RecordCommand>(_ => { });
        Receive<StopRecordingCommand>(_ => { throw errorProvider(); });
    }

    public static Props Props(Func<Exception> errorProvider) => Akka.Actor.Props.Create(() => new ConfigurableErrorAudioRecordingActor(errorProvider));
}

public class ConfigurableErrorTranscribingActor : ReceiveActor
{
    public ConfigurableErrorTranscribingActor(Func<Exception> errorProvider)
    {
        Receive<TranscribeCommand>(_ => { throw errorProvider(); });
    }

    public static Props Props(Func<Exception> errorProvider) => Akka.Actor.Props.Create(() => new ConfigurableErrorTranscribingActor(errorProvider));
}

public class ConfigurableErrorPostProcessorActor : ReceiveActor
{
    public ConfigurableErrorPostProcessorActor(Func<Exception> errorProvider)
    {
        Receive<PostProcessCommand>(_ => { throw errorProvider(); });
    }

    public static Props Props(Func<Exception> errorProvider) => Akka.Actor.Props.Create(() => new ConfigurableErrorPostProcessorActor(errorProvider));
}

public class ConfigurableErrorResultSaverActor : ReceiveActor
{
    public ConfigurableErrorResultSaverActor(Func<Exception> errorProvider)
    {
        Receive<ResultAvailableEvent>(_ => { throw errorProvider(); });
    }

    public static Props Props(Func<Exception> errorProvider) => Akka.Actor.Props.Create(() => new ConfigurableErrorResultSaverActor(errorProvider));
}
