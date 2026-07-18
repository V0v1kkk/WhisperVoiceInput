using System;
using System.IO;
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
public class CancelPipelineTests : AkkaTestBase
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

    private TestFSMRef<MainOrchestratorActor, AppState, StateData> CreateOrchestrator(
        TestActorPropsFactory propsFactory,
        AppSettings settings,
        string actorName)
    {
        return ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                propsFactory,
                _mockClipboardService,
                MockWaylandClient,
                Logger,
                settings,
                TestRetrySettings,
                _observerProbe.Ref)),
            actorName);
    }

    [Test]
    public void Should_Cancel_During_Recording_And_Return_To_Idle()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-recording-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Idle,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Cancel_During_Transcribing_And_Return_To_Idle()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-transcribing-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new AudioRecordedEvent("test.wav"));
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Idle,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Cancel_During_PostProcessing_And_Return_To_Idle()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var settings = CreateSettingsWithPostProcessing();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "cancel-postprocessing-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new AudioRecordedEvent("test.wav"));
        orchestrator.Tell(new TranscriptionCompletedEvent("Hello world"));
        orchestrator.StateName.Should().Be(AppState.PostProcessing);

        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Idle,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Cancel_During_Saving_And_Return_To_Idle()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-saving-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new AudioRecordedEvent("test.wav"));
        orchestrator.Tell(new TranscriptionCompletedEvent("Hello world"));
        orchestrator.StateName.Should().Be(AppState.Saving);

        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Idle,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Ignore_Cancel_In_Idle_State()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-idle-ignore-test");

        orchestrator.StateName.Should().Be(AppState.Idle);

        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);

        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromMilliseconds(500),
            evt => evt is StateUpdatedEvent stateEvent ? stateEvent : null!,
            10);
        stateEvents.Should().NotContain(
            evt => evt.State == AppState.Error,
            "cancel in Idle should not publish an error state");
    }

    [Test]
    public void Should_Retain_Audio_File_On_Cancel_When_KeepLastRecording_Enabled()
    {
        var tempFile = CreateTempAudioFile();
        try
        {
            var propsFactory = new TestActorPropsFactory(this);
            var settings = CreateSettingsWithKeepLastRecording();
            var orchestrator = CreateOrchestrator(propsFactory, settings, "cancel-keep-recording-test");

            orchestrator.Tell(new ToggleCommand());
            orchestrator.Tell(new RecordingStartedEvent(tempFile));
            orchestrator.Tell(new AudioRecordedEvent(tempFile));
            orchestrator.StateName.Should().Be(AppState.Transcribing);

            orchestrator.Tell(new CancelPipelineCommand());

            orchestrator.StateName.Should().Be(AppState.Idle);
            File.Exists(tempFile).Should().BeTrue("audio file should be retained when KeepLastRecording is enabled");

            var reprocessEvent = _observerProbe.FishForMessage<ReprocessAvailableEvent>(
                evt => evt.IsAvailable,
                max: TimeSpan.FromSeconds(2));
            reprocessEvent.IsAvailable.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public void Should_Delete_Audio_File_On_Cancel_When_KeepLastRecording_Disabled()
    {
        var tempFile = CreateTempAudioFile();
        File.Exists(tempFile).Should().BeTrue();

        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-delete-recording-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        File.Exists(tempFile).Should().BeFalse("audio file should be deleted when KeepLastRecording is disabled");
    }

    [Test]
    public void Should_Unstash_Settings_After_Cancel()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-unstash-settings-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        var newSettings = TestSettings with { Model = "whisper-large-v3" };
        orchestrator.Tell(new UpdateSettingsCommand(newSettings));

        orchestrator.Tell(new CancelPipelineCommand());

        AwaitAssert(
            () => orchestrator.StateName.Should().Be(AppState.Idle),
            TimeSpan.FromSeconds(2));
        AwaitAssert(
            () => orchestrator.StateData.FrozenSettings.Model.Should().Be("whisper-large-v3"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Return_To_Idle_When_Cancelled_Immediately_After_Toggle()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-immediate-after-toggle-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new CancelPipelineCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Idle,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Show_Cancelled_By_User_Error_Message()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "cancel-user-message-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new AudioRecordedEvent("test.wav"));
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        orchestrator.Tell(new CancelPipelineCommand());

        var errorEvent = _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Error && evt.ErrorMessage == "Cancelled by user",
            max: TimeSpan.FromSeconds(2));
        errorEvent.ErrorMessage.Should().Be("Cancelled by user");

        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Idle,
            max: TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Idle);
    }
}
