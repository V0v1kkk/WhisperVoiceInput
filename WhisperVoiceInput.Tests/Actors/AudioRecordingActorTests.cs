using System.Reflection;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;
using WhisperVoiceInput.Tests.TestBase;

namespace WhisperVoiceInput.Tests.Actors;

/// <summary>
/// Tests for AudioRecordingActor TryDeleteFile gating by KeepLastRecording (Fix 4).
/// </summary>
[TestFixture]
public class AudioRecordingActorTests : AkkaTestBase
{
    private SoundFlowAudioService _audioService = null!;
    private readonly List<string> _tempFilesToCleanup = [];

    private static readonly SupervisorStrategy StopOnFailureStrategy = new OneForOneStrategy(
        maxNrOfRetries: 0,
        withinTimeRange: TimeSpan.Zero,
        localOnlyDecider: _ => Directive.Stop);

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _audioService = new SoundFlowAudioService(Logger);
        _tempFilesToCleanup.Clear();
    }

    [TearDown]
    public override void TearDown()
    {
        _audioService.Dispose();

        foreach (var path in _tempFilesToCleanup)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        base.TearDown();
    }

    [Test]
    public void Should_Retain_Audio_File_On_Recording_Timeout_When_KeepLastRecording_True()
    {
        var settings = CreateSettingsWithKeepLastRecording() with { RecordingTimeoutMinutes = 1 };
        var actorRef = CreateActor(settings);
        var filePath = StartRecording(actorRef);

        actorRef.Tell(new RecordingTimeout());

        AwaitAssert(
            () => File.Exists(filePath).Should().BeTrue("audio file should be retained on timeout when KeepLastRecording is enabled"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Delete_Audio_File_On_Recording_Timeout_When_KeepLastRecording_False()
    {
        var settings = TestSettings with { KeepLastRecording = false, RecordingTimeoutMinutes = 1 };
        var actorRef = CreateActor(settings);
        var filePath = StartRecording(actorRef);

        actorRef.Tell(new RecordingTimeout());

        AwaitAssert(
            () => File.Exists(filePath).Should().BeFalse("audio file should be deleted on timeout when KeepLastRecording is disabled"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Retain_Audio_File_On_Stop_Recording_Failure_When_KeepLastRecording_True()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var actorRef = CreateActor(settings);
        var filePath = StartRecording(actorRef);

        ForceStopRecordingFailure(actorRef);
        actorRef.Tell(new StopRecordingCommand());

        AwaitAssert(
            () => File.Exists(filePath).Should().BeTrue("audio file should be retained on stop failure when KeepLastRecording is enabled"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Delete_Audio_File_On_Stop_Recording_Failure_When_KeepLastRecording_False()
    {
        var settings = TestSettings with { KeepLastRecording = false };
        var actorRef = CreateActor(settings);
        var filePath = StartRecording(actorRef);

        ForceStopRecordingFailure(actorRef);
        actorRef.Tell(new StopRecordingCommand());

        AwaitAssert(
            () => File.Exists(filePath).Should().BeFalse("audio file should be deleted on stop failure when KeepLastRecording is disabled"),
            TimeSpan.FromSeconds(2));
    }

    private TestActorRef<AudioRecordingActor> CreateActor(AppSettings settings)
    {
        return ActorOfAsTestActorRef<AudioRecordingActor>(
            Props.Create(() => new AudioRecordingActor(settings, Logger, _audioService))
                .WithDispatcher(CallingThreadDispatcher.Id)
                .WithSupervisorStrategy(StopOnFailureStrategy),
            TestActor,
            "audio-recording");
    }

    private string StartRecording(TestActorRef<AudioRecordingActor> actorRef)
    {
        actorRef.Tell(new RecordCommand());
        var started = ExpectMsg<RecordingStartedEvent>(TimeSpan.FromSeconds(5));

        _tempFilesToCleanup.Add(started.FilePath);
        File.Exists(started.FilePath).Should().BeTrue("recording should create a temp audio file");

        return started.FilePath;
    }

    private static void ForceStopRecordingFailure(TestActorRef<AudioRecordingActor> actorRef)
    {
        var actor = actorRef.UnderlyingActor;
        var actorType = typeof(AudioRecordingActor);

        actorType.GetField("_recorder", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(actor, null);
        actorType.GetField("_captureDevice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(actor, null);
    }
}
