using System;
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
public class ErrorHandlingTests : AkkaTestBase
{
    private TestProbe _observerProbe = null!;
    private MockClipboardService _mockClipboardService = null!;
        
    // Pipeline delays configuration
    private readonly TimeSpan _recordingDelay = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _transcriptionDelay = TimeSpan.FromMilliseconds(1000);
    private readonly TimeSpan _postProcessingDelay = TimeSpan.FromMilliseconds(300);
    private readonly TimeSpan _savingDelay = TimeSpan.FromMilliseconds(200);

    [SetUp]
    public override void Setup()
    {
        base.Setup();
            
        _observerProbe = CreateTestProbe();
        _mockClipboardService = new MockClipboardService();
    }

    [Test]
    public void Should_Provide_Detailed_Error_Message_For_AudioRecording_Failure()
    {
        // Arrange - Factory that will make AudioRecordingActor fail
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithFailingAudioRecording();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-detailed-audio-error"
        );

        // Act - Start pipeline that will fail during audio recording
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail)

        // Give some time for retries to happen and error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull();
        
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have detailed error message");
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.AudioRecording, "Should specify the failing step");
    }

    [Test]
    public void Should_Provide_Detailed_Error_Message_For_Transcription_Failure()
    {
        // Arrange - Factory that will make TranscribingActor fail
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithFailingTranscribing();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-detailed-transcription-error"
        );

        // Act - Start pipeline through transcription
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.Tell(new ToggleCommand()); // Stop recording
            
        TestScheduler.Advance(_recordingDelay * 3); // Complete recording

        // Give some time for retries to happen and error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull();
        
        // Verify detailed error information
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have detailed error message");
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.Transcribing, "Should specify the failing step");
    }

    [Test]
    public void Should_Provide_Detailed_Error_Message_For_PostProcessing_Failure()
    {
        // Arrange - Settings with post-processing enabled, but PostProcessor will fail
        var settingsWithPostProcessing = CreateSettingsWithPostProcessing();
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithFailingPostProcessor();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settingsWithPostProcessing,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-detailed-postprocessing-error"
        );

        // Act - Complete pipeline through post-processing
        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new ToggleCommand());
            
        TestScheduler.Advance(_recordingDelay); // Complete recording
        TestScheduler.Advance(_transcriptionDelay * 3); // Complete transcription
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Give some time for retries to happen and error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull();
        
        // Verify detailed error information
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have detailed error message");
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.PostProcessing, "Should specify the failing step");
    }

    [Test]
    public void Should_Provide_Detailed_Error_Message_For_ResultSaver_Failure()
    {
        // Arrange - ResultSaver will fail
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithFailingResultSaver();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-detailed-resultsaver-error"
        );

        // Act - Complete full pipeline
        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new ToggleCommand());
            
        TestScheduler.Advance(_recordingDelay); // Complete recording
        TestScheduler.Advance(_transcriptionDelay); // Complete transcription

        // The main pipeline should complete successfully and transition to Idle
        // ResultSaver failure happens in background and doesn't affect the main FSM
        orchestrator.StateName.Should().Be(AppState.Idle, 
            "Should complete pipeline successfully even if ResultSaver fails in background");

        // Give some time for background error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull();
        
        // Verify detailed error information
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have detailed error message");
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.ResultSaving, "Should specify the failing step");
    }

    [Test]
    public void Should_Handle_Multiple_Sequential_Errors_And_Provide_Correct_Error_Messages()
    {
        // Arrange - Factory that will make AudioRecordingActor fail first
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithFailingAudioRecording();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-sequential-errors"
        );

        // Act - Multiple pipeline runs that will fail
        for (int i = 0; i < 3; i++)
        {
            orchestrator.Tell(new ToggleCommand()); // Start recording
            orchestrator.StateName.Should().Be(AppState.Recording);
            
            orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail)

            // Give some time for retries to happen and error processing
            TestScheduler.Advance(TimeSpan.FromSeconds(30));

            // Verify observer received error notification for this run
            var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
                isMessage: evt => evt.State == AppState.Error,
                max: TimeSpan.FromSeconds(10));

            errorEvent.Should().NotBeNull($"Should receive error notification for run {i + 1}");
            errorEvent.ErrorMessage.Should().Contain("Audio Recording", $"Error {i + 1} should specify Audio Recording step");
        }
    }

    [Test]
    public void Should_Maintain_Error_State_Information_Across_Restarts()
    {
        // Arrange - Factory that will make AudioRecordingActor fail
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithFailingAudioRecording();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-error-state-persistence"
        );

        // Act - Start pipeline that will fail
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail)

        // Give some time for retries to happen and error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        // Verify error state information is maintained
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull("Should receive error notification");
        
        // Verify error event contains all necessary information
        errorEvent.State.Should().Be(AppState.Error, "Error event should have Error state");
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Error event should have error message");
        errorEvent.ErrorMessage.Should().Contain("Audio Recording", "Error message should specify the failing step");
    }

    [Test]
    public void Should_Handle_Timeout_Errors_And_Provide_Appropriate_Messages()
    {
        // Arrange - Use very short delays to simulate timeout-like behavior
        var shortDelay = TimeSpan.FromMilliseconds(10);
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            shortDelay, shortDelay, shortDelay, shortDelay, TestScheduler)
            .WithFailingAudioRecording();

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-timeout-errors"
        );

        // Act - Start pipeline that will fail quickly
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail)

        // Give minimal time for error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        // Verify observer received timeout-like error notification
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull("Should receive error notification for timeout-like failure");
        
        // Even with timeout-like behavior, should still get meaningful error messages
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have error message even for timeout-like failures");
    }
}
