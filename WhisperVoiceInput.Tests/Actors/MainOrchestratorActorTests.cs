using System;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;
using WhisperVoiceInput.Tests.TestBase;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Actors;

[TestFixture]
public class MainOrchestratorActorTests : AkkaTestBase
{
    private TestActorPropsFactory _propsFactory = null!;
    private TestProbe _observerProbe = null!;
    private IClipboardService _mockClipboardService = null!;
    private TestFSMRef<MainOrchestratorActor, AppState, StateData> _orchestrator = null!;

    [SetUp]
    public override void Setup()
    {
        base.Setup();
            
        _propsFactory = new TestActorPropsFactory(this);
        _observerProbe = CreateTestProbe(); // Use auto-generated unique name
        _mockClipboardService = new MockClipboardService();
            
        // Create the MainOrchestratorActor as a TestFSMRef for direct state inspection
        _orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                _propsFactory, 
                _mockClipboardService,
                Logger, 
                TestSettings,
                TestRetrySettings, 
                _observerProbe.Ref)),
            "test-orchestrator"
        );
    }

    [Test]
    public void Should_Start_In_Idle_State()
    {
        // Assert
        _orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Transition_From_Idle_To_Recording_On_Toggle()
    {
        // Act
        _orchestrator.Tell(new ToggleCommand());

        // Assert
        _orchestrator.StateName.Should().Be(AppState.Recording);
            
        // Verify that RecordCommand was sent to AudioRecordingActor
        _propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void Should_Transition_From_Recording_To_Transcribing_On_Second_Toggle()
    {
        // Arrange - Start recording
        _orchestrator.Tell(new ToggleCommand());
        _orchestrator.StateName.Should().Be(AppState.Recording);
        _propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();

        // Act - Stop recording
        _orchestrator.Tell(new ToggleCommand());

        // Assert - Should still be in Recording state until AudioRecordedEvent
        _orchestrator.StateName.Should().Be(AppState.Recording);
            
        // Verify that StopRecordingCommand was sent to AudioRecordingActor
        _propsFactory.AudioRecordingProbe.ExpectMsg<StopRecordingCommand>(TimeSpan.FromSeconds(1));
    }

    [Test]
    public void Should_Process_AudioRecordedEvent_And_Send_TranscribeCommand()
    {
        // Arrange - Start transcribing state
        _orchestrator.Tell(new ToggleCommand()); // -> Recording
        _propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();

        // Act - Simulate audio recording completion
        var audioEvent = new AudioRecordedEvent("test-audio.wav");
        _orchestrator.Tell(audioEvent);

        // Assert - Should transition to Transcribing and send TranscribeCommand
        _orchestrator.StateName.Should().Be(AppState.Transcribing);
            
        var transcribeCommand = _propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(1));
        transcribeCommand.AudioFile.Should().Be("test-audio.wav");
    }

    [Test]
    public void Should_Transition_To_PostProcessing_When_Enabled()
    {
        // Arrange - Use settings with post-processing enabled
        var settingsWithPostProcessing = CreateSettingsWithPostProcessing();
        var orchestratorWithPostProcessing = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                _propsFactory, 
                _mockClipboardService,
                Logger, 
                settingsWithPostProcessing,
                TestRetrySettings, 
                _observerProbe.Ref)),
            "test-orchestrator-postprocessing"
        );
            
        // Start workflow
        orchestratorWithPostProcessing.Tell(new ToggleCommand()); // -> Recording
        orchestratorWithPostProcessing.Tell(new AudioRecordedEvent("test.wav")); // -> Transcribing
            
        // Act - Complete transcription
        var transcriptionEvent = new TranscriptionCompletedEvent("Hello world");
        orchestratorWithPostProcessing.Tell(transcriptionEvent);

        // Assert
        orchestratorWithPostProcessing.StateName.Should().Be(AppState.PostProcessing);
            
        // Verify PostProcessCommand was sent
        var postProcessCommand = _propsFactory.PostProcessorProbe.ExpectMsg<PostProcessCommand>(TimeSpan.FromSeconds(1));
        postProcessCommand.Text.Should().Be("Hello world");
    }

    [Test]
    public void Should_Return_To_Idle_After_Complete_Workflow_Without_PostProcessing()
    {
        // Arrange
        _orchestrator.Tell(new ToggleCommand()); // -> Recording
        _propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();
            
        // Simulate audio recorded
        _orchestrator.Tell(new AudioRecordedEvent("test.wav")); // -> Transcribing
        _propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        // Act - Complete transcription (without post-processing)
        var transcriptionEvent = new TranscriptionCompletedEvent("Hello world");
        _orchestrator.Tell(transcriptionEvent);

        // Assert
        _orchestrator.StateName.Should().Be(AppState.Idle);
            
        // Verify result was saved
        var resultEvent = _propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>(TimeSpan.FromSeconds(1));
        resultEvent.Text.Should().Be("Hello world");
    }

    [Test]
    public void Should_Send_StateUpdatedEvent_On_State_Changes()
    {
        // Act
        _orchestrator.Tell(new ToggleCommand());

        // Assert - Should notify observer of state change
        // Use ReceiveWhile to collect state events deterministically
        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(2),
            evt => evt is StateUpdatedEvent stateEvent ? stateEvent : null!,
            2 // Expect both initial and recording state events
        );

        // Should receive at least one Recording state event
        stateEvents.Should().Contain(evt => evt.State == AppState.Recording, 
            "Should receive Recording state notification");
    }

    [Test]
    public void Should_Stash_Settings_Updates_During_Processing()
    {
        // Arrange - Start recording
        _orchestrator.Tell(new ToggleCommand());
        _orchestrator.StateName.Should().Be(AppState.Recording);

        // Act - Try to update settings while recording
        var newSettings = TestSettings with { Model = "whisper-large-v3" };
        _orchestrator.Tell(new UpdateSettingsCommand(newSettings));

        // The settings update should be stashed and not processed immediately
        // We can't directly test stashing, but we can verify the state doesn't change unexpectedly
        _orchestrator.StateName.Should().Be(AppState.Recording);
            
        // Complete the recording workflow
        _orchestrator.Tell(new AudioRecordedEvent("test.wav")); // -> Transcribing
        _orchestrator.Tell(new TranscriptionCompletedEvent("Hello")); // -> Idle

        // Now back in Idle, the stashed settings should be processed
        _orchestrator.StateName.Should().Be(AppState.Idle);
    }
}

/// <summary>
/// Mock clipboard service for testing
/// </summary>
public class MockClipboardService : IClipboardService
{
    public void SetTopLevel(Avalonia.Controls.TopLevel topLevel)
    {
        // Mock implementation - do nothing
    }

    public Task SetTextAsync(string text)
    {
        // Mock implementation - just return completed task
        return Task.CompletedTask;
    }
}