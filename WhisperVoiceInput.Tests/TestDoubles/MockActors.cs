using System;
using Akka.Actor;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Mock actors that simulate realistic behavior with configurable delays
/// for testing the complete pipeline workflow
/// </summary>
public class MockAudioRecordingActor : ReceiveActor
{
    private readonly TimeSpan _recordingDelay;
    private readonly IScheduler _scheduler;
    private bool _isRecording = false;

    public MockAudioRecordingActor(TimeSpan recordingDelay, IScheduler scheduler)
    {
        _recordingDelay = recordingDelay;
        _scheduler = scheduler;
            
        Receive<RecordCommand>(HandleRecord);
        Receive<StopRecordingCommand>(HandleStopRecording);
        Receive<DelayedRecordingComplete>(HandleDelayedComplete);
    }

    private void HandleRecord(RecordCommand command)
    {
        if (!_isRecording)
        {
            _isRecording = true;
            // Recording starts immediately, no event sent until stop
        }
    }

    private void HandleStopRecording(StopRecordingCommand command)
    {
        if (_isRecording)
        {
            _isRecording = false;
            var originalSender = Context.Parent;
                
            // Schedule the completion directly to the parent after recording delay
            _scheduler.ScheduleTellOnce(
                _recordingDelay,
                originalSender,
                new AudioRecordedEvent($"mock-audio-{DateTime.Now.Ticks}.wav"),
                Self);
        }
    }

    private void HandleDelayedComplete(DelayedRecordingComplete delayed)
    {
        // This method is no longer needed but kept for compatibility
        delayed.OriginalSender.Tell(new AudioRecordedEvent($"mock-audio-{DateTime.Now.Ticks}.wav"));
    }

    public static Props Props(TimeSpan delay, IScheduler scheduler)
        => Akka.Actor.Props.Create(() => new MockAudioRecordingActor(delay, scheduler));

    private class DelayedRecordingComplete
    {
        public IActorRef OriginalSender { get; }
        public DelayedRecordingComplete(IActorRef originalSender) => OriginalSender = originalSender;
    }
}

public class MockTranscribingActor : ReceiveActor
{
    private readonly TimeSpan _transcriptionDelay;
    private readonly IScheduler _scheduler;

    public MockTranscribingActor(TimeSpan transcriptionDelay, IScheduler scheduler)
    {
        _transcriptionDelay = transcriptionDelay;
        _scheduler = scheduler;
            
        Receive<TranscribeCommand>(HandleTranscribe);
        Receive<DelayedTranscriptionComplete>(HandleDelayedComplete);
    }

    private void HandleTranscribe(TranscribeCommand command)
    {
        var originalSender = Context.Parent;
            
        // Schedule the completion directly to the parent after transcription delay
        _scheduler.ScheduleTellOnce(
            _transcriptionDelay,
            originalSender,
            new TranscriptionCompletedEvent($"Mock transcription of {command.AudioFile}"),
            Self);
    }

    private void HandleDelayedComplete(DelayedTranscriptionComplete delayed)
    {
        // This method is no longer needed but kept for compatibility
        delayed.OriginalSender.Tell(new TranscriptionCompletedEvent($"Mock transcription of {delayed.AudioFile}"));
    }

    public static Props Props(TimeSpan delay, IScheduler scheduler)
        => Akka.Actor.Props.Create(() => new MockTranscribingActor(delay, scheduler));

    private class DelayedTranscriptionComplete
    {
        public IActorRef OriginalSender { get; }
        public string AudioFile { get; }
        public DelayedTranscriptionComplete(IActorRef originalSender, string audioFile)
        {
            OriginalSender = originalSender;
            AudioFile = audioFile;
        }
    }
}

public class MockPostProcessorActor : ReceiveActor
{
    private readonly TimeSpan _processingDelay;
    private readonly IScheduler _scheduler;

    public MockPostProcessorActor(TimeSpan processingDelay, IScheduler scheduler)
    {
        _processingDelay = processingDelay;
        _scheduler = scheduler;
            
        Receive<PostProcessCommand>(HandlePostProcess);
        Receive<DelayedPostProcessComplete>(HandleDelayedComplete);
    }

    private void HandlePostProcess(PostProcessCommand command)
    {
        var originalSender = Context.Parent;
            
        // Schedule the completion directly to the parent after processing delay
        _scheduler.ScheduleTellOnce(
            _processingDelay,
            originalSender,
            new PostProcessedEvent($"Enhanced: {command.Text}"),
            Self);
    }

    private void HandleDelayedComplete(DelayedPostProcessComplete delayed)
    {
        // This method is no longer needed but kept for compatibility
        delayed.OriginalSender.Tell(new PostProcessedEvent($"Enhanced: {delayed.OriginalText}"));
    }

    public static Props Props(TimeSpan delay, IScheduler scheduler)
        => Akka.Actor.Props.Create(() => new MockPostProcessorActor(delay, scheduler));

    private class DelayedPostProcessComplete
    {
        public IActorRef OriginalSender { get; }
        public string OriginalText { get; }
        public DelayedPostProcessComplete(IActorRef originalSender, string originalText)
        {
            OriginalSender = originalSender;
            OriginalText = originalText;
        }
    }
}

public class MockResultSaverActor : ReceiveActor
{
    private readonly TimeSpan _savingDelay;
    private readonly IScheduler _scheduler;

    public MockResultSaverActor(TimeSpan savingDelay, IScheduler scheduler)
    {
        _savingDelay = savingDelay;
        _scheduler = scheduler;
            
        Receive<ResultAvailableEvent>(HandleSaveResult);
        Receive<DelayedSaveComplete>(HandleDelayedComplete);
    }

    private void HandleSaveResult(ResultAvailableEvent evt)
    {
        var originalSender = Context.Parent;
            
        // Schedule the completion directly to the parent after saving delay
        _scheduler.ScheduleTellOnce(
            _savingDelay,
            originalSender,
            new ResultSavedEvent(evt.Text),
            Self);
    }

    private void HandleDelayedComplete(DelayedSaveComplete delayed)
    {
        // This method is no longer needed but kept for compatibility
        delayed.OriginalSender.Tell(new ResultSavedEvent(delayed.Text));
    }

    public static Props Props(TimeSpan delay, IScheduler scheduler)
        => Akka.Actor.Props.Create(() => new MockResultSaverActor(delay, scheduler));

    private class DelayedSaveComplete
    {
        public IActorRef OriginalSender { get; }
        public string Text { get; }
        public DelayedSaveComplete(IActorRef originalSender, string text)
        {
            OriginalSender = originalSender;
            Text = text;
        }
    }
}

// Failing actors for testing supervision
public class FailingAudioRecordingActor : ReceiveActor
{
    public FailingAudioRecordingActor()
    {
        Receive<RecordCommand>(_ => { });
        Receive<StopRecordingCommand>(_ => throw new InvalidOperationException("Audio recording failed"));
    }

    public static Props Props() => Akka.Actor.Props.Create<FailingAudioRecordingActor>();

    protected override void PreRestart(Exception reason, object message)
    {
        Self.Tell(message);
        base.PreRestart(reason, message);
    }
}

public class FailingTranscribingActor : ReceiveActor
{
    public FailingTranscribingActor()
    {
        Receive<TranscribeCommand>(_ => throw new InvalidOperationException("Transcription failed"));
    }

    public static Props Props() => Akka.Actor.Props.Create<FailingTranscribingActor>();
    
    protected override void PreRestart(Exception reason, object message)
    {
        Self.Tell(message);
        base.PreRestart(reason, message);
    }
}

public class FailingPostProcessorActor : ReceiveActor
{
    public FailingPostProcessorActor()
    {
        Receive<PostProcessCommand>(_ => throw new InvalidOperationException("Post-processing failed"));
    }

    public static Props Props() => Akka.Actor.Props.Create<FailingPostProcessorActor>();
    
    protected override void PreRestart(Exception reason, object message)
    {
        Self.Tell(message);
        base.PreRestart(reason, message);
    }
}

public class FailingResultSaverActor : ReceiveActor
{
    public FailingResultSaverActor()
    {
        Receive<ResultAvailableEvent>(_ => throw new InvalidOperationException("Result saving failed"));
    }

    public static Props Props() => Akka.Actor.Props.Create<FailingResultSaverActor>();
    
    protected override void PreRestart(Exception reason, object message)
    {
        Self.Tell(message);
        base.PreRestart(reason, message);
    }
}