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
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Tests.Actors;

/// <summary>
/// Tests for specific error scenarios and edge cases in the pipeline
/// </summary>
[TestFixture]
public class SpecificErrorScenariosTests : AkkaTestBase
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
    public void Should_Handle_FileNotFoundException_In_Transcription_And_Stop_Actor()
    {
        // Arrange - Define expected error message as constant
        const string expectedErrorMessage = "Audio file not found";
        const string expectedFileName = "test-file.wav";
        
        var failingPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithTranscribingActorThatThrows(() => new FileNotFoundException(expectedErrorMessage, expectedFileName));

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    failingPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-file-not-found"
        );

        // Act - Start pipeline that will cause FileNotFoundException
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.Tell(new ToggleCommand()); // Stop recording
            
        TestScheduler.Advance(_recordingDelay); // Complete recording
        
        // Give some time for error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(3));

        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull();
        
        // Verify specific error handling for FileNotFoundException
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.Transcribing, "Should specify the failing step");
        errorEvent.ErrorMessage.Should().Contain(expectedErrorMessage, "Should include the exception message we threw");
    }

    [Test]
    public void Should_Handle_Network_Timeout_Error_In_Transcription()
    {
        // Arrange - Define expected error message as constant
        const string expectedErrorMessage = "Network timeout during transcription request";
        
        // Use minimal retry settings to speed up the test
        var fastRetrySettings = new RetryPolicySettings
        {
            MaxRetries = 0, // No retries for faster test execution
            RetryTimeWindow = TimeSpan.FromMilliseconds(100),
            StrategyType = SupervisionStrategyType.OneForOne
        };
        
        var timeoutPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithTranscribingActorThatThrows(() => new TimeoutException(expectedErrorMessage));

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    timeoutPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    fastRetrySettings, // Use fast settings
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-network-timeout"
        );

        // Act - Start pipeline that will timeout
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.Tell(new ToggleCommand()); // Stop recording
            
        TestScheduler.Advance(_recordingDelay); // Complete recording

        // With no retries, error should happen quickly - just wait for Error event
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(5));

        errorEvent.Should().NotBeNull("Should receive error notification for network timeout");
        
        // Verify timeout-specific error information
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.Transcribing, "Should specify the failing step");
        errorEvent.ErrorMessage.Should().Contain(expectedErrorMessage, "Should include the original exception message");
    }

    [Test]
    public void Should_Handle_Authentication_Error_In_Transcription()
    {
        // Arrange - Define expected error message as constant
        const string expectedErrorMessage = "Invalid API key or authentication failed";
        
        // Use minimal retry settings to speed up the test
        var fastRetrySettings = new RetryPolicySettings
        {
            MaxRetries = 0, // No retries for faster test execution
            RetryTimeWindow = TimeSpan.FromMilliseconds(100),
            StrategyType = SupervisionStrategyType.OneForOne
        };
        
        var authErrorPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithTranscribingActorThatThrows(() => new UnauthorizedAccessException(expectedErrorMessage));

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    authErrorPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    fastRetrySettings, // Use fast settings
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-auth-error"
        );

        // Act - Start pipeline that will fail authentication
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.Tell(new ToggleCommand()); // Stop recording
            
        TestScheduler.Advance(_recordingDelay); // Complete recording

        // With no retries, error should happen quickly - just wait for Error event
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(5));

        errorEvent.Should().NotBeNull();
        
        // Verify authentication-specific error information
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have detailed error message");
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.Transcribing, "Should specify the failing step");
        errorEvent.ErrorMessage.Should().Contain(expectedErrorMessage, "Should include the exception message we threw");
    }


    [Test]
    public void Should_Handle_Multiple_Concurrent_Errors_With_Different_Types()
    {
        // Arrange - Define expected error message as constant
        const string expectedErrorMessage = "Multiple errors occurred";
        
        // Use minimal retry settings to speed up the test
        var fastRetrySettings = new RetryPolicySettings
        {
            MaxRetries = 0, // No retries for faster test execution
            RetryTimeWindow = TimeSpan.FromMilliseconds(100),
            StrategyType = SupervisionStrategyType.OneForOne
        };
        
        var multiErrorPropsFactory = new ConfigurableErrorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler)
            .WithAudioRecordingActorThatThrows(() => new AggregateException(expectedErrorMessage,
                new InvalidOperationException("Primary recording error"),
                new TimeoutException("Backup recording timeout")));

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    multiErrorPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    fastRetrySettings, // Use fast settings
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-multi-type-errors"
        );

        // Act - Start pipeline that will encounter multiple error types
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail with first error type)

        // With no retries, error should happen quickly
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(5));

        errorEvent.Should().NotBeNull();
        
        // Should get a general error message that covers the failure
        errorEvent.ErrorMessage.Should().NotBeNullOrEmpty("Should have error message");
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.AudioRecording, "Should specify the failing step");
    }

    [Test]
    public void Should_Preserve_Error_Context_After_Settings_Changes()
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
            "test-orchestrator-error-context-preservation"
        );

        // Act - Start pipeline that will fail, then send settings updates
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail)

        // Send multiple settings updates while in error state
        var firstUpdate = TestSettings with { Model = "whisper-small" };
        var secondUpdate = TestSettings with { Model = "whisper-medium" };
            
        orchestrator.Tell(new UpdateSettingsCommand(firstUpdate));
        orchestrator.Tell(new UpdateSettingsCommand(secondUpdate));

        // Give some time for error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        // Verify observer still receives the original error notification
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromMinutes(10));

        errorEvent.Should().NotBeNull("Should receive original error notification despite settings changes");
        
        // Error information should be preserved
        errorEvent.ErrorMessage.Should().Contain(MainOrchestratorActor.StepNames.AudioRecording, "Should preserve original error context");
    }

    [Test]
    public void Should_Handle_Error_Recovery_With_New_Settings()
    {
        // Arrange - First with failing factory, then simulate recovery with new settings
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
            "test-orchestrator-error-recovery"
        );

        // Act - First run fails
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // Stop recording (will fail)

        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        // Send new settings that might fix the issue
        var fixedSettings = TestSettings with { ServerAddress = "http://localhost:9001/v1/audio/transcriptions" };
        orchestrator.Tell(new UpdateSettingsCommand(fixedSettings));

        // Give time for error processing
        TestScheduler.Advance(TimeSpan.FromSeconds(30));

        // Verify error was reported before settings change
        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            isMessage: evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(10));

        errorEvent.Should().NotBeNull("Should have reported error before settings change");
    }
}
