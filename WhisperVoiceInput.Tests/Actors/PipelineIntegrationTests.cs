using System;
using System.Linq;
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
public class PipelineIntegrationTests : AkkaTestBase
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
    public void Should_Complete_Full_Pipeline_Without_PostProcessing()
    {
        // Arrange
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings, // PostProcessing disabled
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-no-postprocessing"
        );

        // Act - Start the pipeline
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        // Act - Stop recording (trigger transcription)
        orchestrator.Tell(new ToggleCommand());
            
        // Advance time to complete recording and wait for AudioRecordedEvent - use 5x to ensure processing
        TestScheduler.Advance(TimeSpan.FromTicks(_recordingDelay.Ticks * 5));
            
        // Give a moment for messages to be processed
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Transcribing), TimeSpan.FromSeconds(1));
            
        // Advance time to complete transcription - use 5x to ensure processing
        TestScheduler.Advance(TimeSpan.FromTicks(_transcriptionDelay.Ticks * 5));
            
        // Wait for transition to Saving
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Saving), TimeSpan.FromSeconds(1));
            
        // Advance time to complete saving - use 5x to ensure processing
        TestScheduler.Advance(TimeSpan.FromTicks(_savingDelay.Ticks * 5));
        
        // Wait for transition to Idle
        AwaitAssert(() => orchestrator.StateName.Should().Be(AppState.Idle), TimeSpan.FromSeconds(1));

        // Verify we received all expected state transitions
        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(1),
            evt => evt is StateUpdatedEvent stateEvent ? stateEvent : null!,
            10 // Allow for multiple state notifications
        );

        // Expected sequence: Idle (initial) -> Recording -> Transcribing -> Saving -> Success -> Idle (final)
        stateEvents.Should().HaveCount(6, "Should receive exactly 6 state notifications");
        stateEvents[0].State.Should().Be(AppState.Idle, "Initial FSM state");
        stateEvents[1].State.Should().Be(AppState.Recording, "Transition to Recording");
        stateEvents[2].State.Should().Be(AppState.Transcribing, "Transition to Transcribing");
        stateEvents[3].State.Should().Be(AppState.Saving, "Transition to Saving");
        stateEvents[4].State.Should().Be(AppState.Success, "Success notification");
        stateEvents[5].State.Should().Be(AppState.Idle, "Final transition to Idle");
    }

    [Test]
    public void Should_Complete_Full_Pipeline_With_PostProcessing()
    {
        // Arrange - Settings with post-processing enabled
        var settingsWithPostProcessing = CreateSettingsWithPostProcessing();
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settingsWithPostProcessing,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-with-postprocessing"
        );

        // Act - Start the complete pipeline
        orchestrator.Tell(new ToggleCommand()); // -> Recording
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        orchestrator.Tell(new ToggleCommand()); // -> Stop recording, start transcription
            
        // Advance through each stage with precise timing
        TestScheduler.Advance(_recordingDelay); // Complete recording
        orchestrator.StateName.Should().Be(AppState.Transcribing);
            
        TestScheduler.Advance(_transcriptionDelay); // Complete transcription
        orchestrator.StateName.Should().Be(AppState.PostProcessing);
            
        TestScheduler.Advance(_postProcessingDelay); // Complete post-processing
        orchestrator.StateName.Should().Be(AppState.Saving);
            
        TestScheduler.Advance(_savingDelay); // Complete saving
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Verify complete state flow
        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(1),
            evt => evt is StateUpdatedEvent stateEvent ? stateEvent : null!,
            10
        );

        // Expected sequence: Idle (initial) -> Recording -> Transcribing -> PostProcessing -> Saving -> Success -> Idle (final)
        stateEvents.Should().HaveCount(7, "Should receive exactly 7 state notifications");
        stateEvents[0].State.Should().Be(AppState.Idle, "Initial FSM state");
        stateEvents[1].State.Should().Be(AppState.Recording, "Transition to Recording");
        stateEvents[2].State.Should().Be(AppState.Transcribing, "Transition to Transcribing");
        stateEvents[3].State.Should().Be(AppState.PostProcessing, "Transition to PostProcessing");
        stateEvents[4].State.Should().Be(AppState.Saving, "Transition to Saving");
        stateEvents[5].State.Should().Be(AppState.Success, "Success notification");
        stateEvents[6].State.Should().Be(AppState.Idle, "Final transition to Idle");
    }

    [Test]
    public void Should_Handle_Settings_Stashing_During_Pipeline_Execution()
    {
        // Arrange
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var initialSettings = TestSettings with { Model = "whisper-base" };
        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    initialSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-stashing"
        );

        // Verify initial state data
        orchestrator.StateData.FrozenSettings.Model.Should().Be("whisper-base");

        // Act - Start pipeline
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        // Send multiple settings updates while recording (all should be stashed)
        var firstUpdate = initialSettings with { Model = "whisper-small" };
        var secondUpdate = initialSettings with { Model = "whisper-medium" };
        var thirdUpdate = initialSettings with { Model = "whisper-large-v3", AudioFilePath = "/updated/audio/path" };
            
        orchestrator.Tell(new UpdateSettingsCommand(firstUpdate));
        orchestrator.Tell(new UpdateSettingsCommand(secondUpdate));
        orchestrator.Tell(new UpdateSettingsCommand(thirdUpdate));

        // Settings should not change during active session (frozen)
        orchestrator.StateData.FrozenSettings.Model.Should().Be("whisper-base", 
            "Settings should remain frozen during active session");

        // Continue pipeline
        orchestrator.Tell(new ToggleCommand()); // Stop recording
            
        // Advance time through the pipeline
        TestScheduler.Advance(_recordingDelay); // Complete recording
        orchestrator.StateName.Should().Be(AppState.Transcribing);
            
        // Settings should still be frozen during transcription
        orchestrator.StateData.FrozenSettings.Model.Should().Be("whisper-base",
            "Settings should remain frozen during transcription");
            
        TestScheduler.Advance(_transcriptionDelay); // Complete transcription
        TestScheduler.Advance(_savingDelay); // Complete saving

        // Should be back in Idle state
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Verify that the last stashed settings are now active
        orchestrator.StateData.FrozenSettings.Model.Should().Be("whisper-large-v3", 
            "Last stashed settings should be applied after pipeline completion");
        orchestrator.StateData.FrozenSettings.AudioFilePath.Should().Be("/updated/audio/path",
            "All properties from last stashed settings should be preserved");

        // Verify the actor remains functional with new settings
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);
            
        // The new session should use the updated settings
        orchestrator.StateData.FrozenSettings.Model.Should().Be("whisper-large-v3",
            "New session should use the last updated settings");
    }

    [Test]
    public void Should_Maintain_Correct_Timing_Throughout_Pipeline()
    {
        // Arrange
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    CreateSettingsWithPostProcessing(),
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-timing"
        );

        // Act & Assert - Verify no premature state transitions
        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        orchestrator.Tell(new ToggleCommand());
            
        // Should not transition immediately after partial time advancement
        TestScheduler.Advance(TimeSpan.FromMilliseconds(250)); // Half recording delay
        orchestrator.StateName.Should().Be(AppState.Recording, "Should still be recording");

        TestScheduler.Advance(TimeSpan.FromMilliseconds(250)); // Complete recording delay
        orchestrator.StateName.Should().Be(AppState.Transcribing, "Should now be transcribing");

        // Test partial transcription time
        TestScheduler.Advance(TimeSpan.FromMilliseconds(500)); // Half transcription delay
        orchestrator.StateName.Should().Be(AppState.Transcribing, "Should still be transcribing");

        TestScheduler.Advance(TimeSpan.FromMilliseconds(500)); // Complete transcription delay
        orchestrator.StateName.Should().Be(AppState.PostProcessing, "Should now be post-processing");

        // Test post-processing timing
        TestScheduler.Advance(TimeSpan.FromMilliseconds(150)); // Half post-processing delay
        orchestrator.StateName.Should().Be(AppState.PostProcessing, "Should still be post-processing");

        TestScheduler.Advance(TimeSpan.FromMilliseconds(150)); // Complete post-processing delay
        orchestrator.StateName.Should().Be(AppState.Saving, "Should now be saving");

        // Test saving timing
        TestScheduler.Advance(TimeSpan.FromMilliseconds(100)); // Half saving delay
        orchestrator.StateName.Should().Be(AppState.Saving, "Should still be saving");

        TestScheduler.Advance(TimeSpan.FromMilliseconds(100)); // Complete saving delay
        orchestrator.StateName.Should().Be(AppState.Idle, "Should now be idle");
    }

    [Test]
    public void Should_Complete_Multiple_Sequential_Pipelines()
    {
        // Arrange
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    TestSettings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-sequential"
        );

        // Act - Execute first pipeline
        ExecuteCompletePipeline(orchestrator);
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Execute second pipeline
        ExecuteCompletePipeline(orchestrator);
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Execute third pipeline
        ExecuteCompletePipeline(orchestrator);
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Verify we received multiple sets of state transitions
        var allStateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(1),
            evt => evt is StateUpdatedEvent stateEvent ? stateEvent : null!,
            50 // Allow for many state notifications from multiple runs
        );

        // Should have received multiple Recording and Transcribing events
        var recordingEvents = allStateEvents.Where(evt => evt.State == AppState.Recording).ToList();
        var transcribingEvents = allStateEvents.Where(evt => evt.State == AppState.Transcribing).ToList();

        recordingEvents.Should().HaveCountGreaterThan(2, "Should have at least 3 recording sessions");
        transcribingEvents.Should().HaveCountGreaterThan(2, "Should have at least 3 transcription sessions");
    }


    private void ExecuteCompletePipeline(TestFSMRef<MainOrchestratorActor, AppState, StateData> orchestrator)
    {
        orchestrator.Tell(new ToggleCommand()); // Start recording
        orchestrator.Tell(new ToggleCommand()); // Stop recording
            
        TestScheduler.Advance(_recordingDelay); // Complete recording
        TestScheduler.Advance(_transcriptionDelay); // Complete transcription
        TestScheduler.Advance(_savingDelay); // Complete saving
    }
}
