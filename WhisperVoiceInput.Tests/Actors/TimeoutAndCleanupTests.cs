using System;
using System.IO;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;
using WhisperVoiceInput.Tests.TestBase;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Actors;

[TestFixture]
public class TimeoutAndCleanupTests : AkkaTestBase
{
    private TestProbe _observerProbe = null!;
    private MockClipboardService _mockClipboardService = null!;

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _observerProbe = CreateTestProbe();
        _mockClipboardService = new MockClipboardService();
    }

    [Test]
    public void Orchestrator_Should_Delete_Audio_File_On_Recording_Timeout()
    {
        // Arrange
        var fastRetrySettings = new RetryPolicySettings
        {
            MaxRetries = 0,
            RetryTimeWindow = TimeSpan.FromMilliseconds(100),
            StrategyType = SupervisionStrategyType.OneForOne
        };

        var propsFactory = new ConfigurableErrorPropsFactory(
            recordingDelay: TimeSpan.FromMilliseconds(1),
            transcriptionDelay: TimeSpan.FromMilliseconds(1),
            postProcessingDelay: TimeSpan.FromMilliseconds(1),
            savingDelay: TimeSpan.FromMilliseconds(1),
            scheduler: TestScheduler)
            .WithAudioRecordingActorThatThrows(() => new UserConfiguredTimeoutException("hard timeout"));

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    propsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    fastRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id));

        // Create a temp file that should be deleted upon failure
        var tempFile = Path.Combine(Path.GetTempPath(), $"wvi_test_{Guid.NewGuid():N}.mp3");
        File.WriteAllText(tempFile, "test");
        File.Exists(tempFile).Should().BeTrue();

        // Act: start recording
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        // Notify orchestrator that recording started and provide path
        orchestrator.Tell(new RecordingStartedEvent(tempFile));

        // Stop recording to trigger failure in audio actor (timeout exception)
        orchestrator.Tell(new ToggleCommand());

        // Give the orchestrator a moment to process termination path
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Idle), TimeSpan.FromSeconds(2));

        // Assert: file should be deleted by orchestrator cleanup
        AwaitAssert(() => File.Exists(tempFile).Should().BeFalse(), TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Orchestrator_Should_Delete_Audio_File_On_Transcribing_Timeout()
    {
        // Arrange
        var fastRetrySettings = new RetryPolicySettings
        {
            MaxRetries = 0,
            RetryTimeWindow = TimeSpan.FromMilliseconds(100),
            StrategyType = SupervisionStrategyType.OneForOne
        };

        var propsFactory = new ConfigurableErrorPropsFactory(
            recordingDelay: TimeSpan.FromMilliseconds(1),
            transcriptionDelay: TimeSpan.FromMilliseconds(1),
            postProcessingDelay: TimeSpan.FromMilliseconds(1),
            savingDelay: TimeSpan.FromMilliseconds(1),
            scheduler: TestScheduler)
            .WithTranscribingActorThatThrows(() => new UserConfiguredTimeoutException("hard timeout"));

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    propsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    fastRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id));

        // Create a temp file that should be deleted upon failure
        var tempFile = Path.Combine(Path.GetTempPath(), $"wvi_test_{Guid.NewGuid():N}.mp3");
        File.WriteAllText(tempFile, "test");
        File.Exists(tempFile).Should().BeTrue();

        // Act: start recording then transition straight to transcribing with our file path
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);
        orchestrator.Tell(new AudioRecordedEvent(tempFile));

        // Orchestrator should attempt to transcribe and then fail unrecoverably
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Idle), TimeSpan.FromSeconds(2));

        // Assert: file should be deleted by orchestrator cleanup
        AwaitAssert(() => File.Exists(tempFile).Should().BeFalse(), TimeSpan.FromSeconds(2));
    }
}


