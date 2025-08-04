using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.NUnit;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.Tests.TestDoubles;

/// <summary>
/// Test implementation of IActorPropsFactory that returns TestProbes instead of real actors.
/// This enables isolated testing of actors without their dependencies.
/// </summary>
public class TestActorPropsFactory : IActorPropsFactory
{
    private readonly TestKit _testKit;

    public TestProbe AudioRecordingProbe { get; }
    public TestProbe TranscribingProbe { get; }
    public TestProbe PostProcessorProbe { get; }
    public TestProbe ResultSaverProbe { get; }
    public TestProbe ObserverProbe { get; }

    public TestActorPropsFactory(TestKit testKit)
    {
        _testKit = testKit;
            
        // Create test probes for each actor type
        AudioRecordingProbe = _testKit.CreateTestProbe();
        TranscribingProbe = _testKit.CreateTestProbe();
        PostProcessorProbe = _testKit.CreateTestProbe();
        ResultSaverProbe = _testKit.CreateTestProbe();
        ObserverProbe = _testKit.CreateTestProbe();
    }

    public Props CreateAudioRecordingActorProps(AppSettings settings)
    {
        // Return props that create a TestActor that forwards to the probe
        return Props.Create(() => new ForwardingActor(AudioRecordingProbe.Ref));
    }

    public Props CreateTranscribingActorProps(AppSettings settings)
    {
        return Props.Create(() => new ForwardingActor(TranscribingProbe.Ref));
    }

    public Props? CreatePostProcessorActorProps(AppSettings settings)
    {
        if (!settings.PostProcessingEnabled)
            return null;
                
        return Props.Create(() => new ForwardingActor(PostProcessorProbe.Ref));
    }

    public Props CreateResultSaverActorProps(AppSettings settings, IClipboardService clipboardService)
    {
        return Props.Create(() => new ForwardingActor(ResultSaverProbe.Ref));
    }

    public Props CreateObserverActorProps()
    {
        return Props.Create(() => new ForwardingActor(ObserverProbe.Ref));
    }
}

/// <summary>
/// Simple actor that forwards all messages to another actor (TestProbe).
/// Used in tests to substitute real actors with TestProbes.
/// </summary>
public class ForwardingActor : ReceiveActor
{
    private readonly IActorRef _target;

    public ForwardingActor(IActorRef target)
    {
        _target = target;
        ReceiveAny(message => _target.Forward(message));
    }
}