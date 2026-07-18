using System;
using System.Collections.Generic;
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
public class KeepLastRecordingTests : AkkaTestBase
{
    private TestProbe _observerProbe = null!;
    private MockClipboardService _mockClipboardService = null!;
    private readonly List<string> _tempFilesToCleanup = [];

    private static readonly TimeSpan FastDelay = TimeSpan.FromMilliseconds(1);

    private static RetryPolicySettings FastRetrySettings => new()
    {
        MaxRetries = 0,
        RetryTimeWindow = TimeSpan.FromMilliseconds(100),
        StrategyType = SupervisionStrategyType.OneForOne
    };

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _observerProbe = CreateTestProbe();
        _mockClipboardService = new MockClipboardService();
        _tempFilesToCleanup.Clear();
    }

    [TearDown]
    public override void TearDown()
    {
        foreach (var path in _tempFilesToCleanup)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        base.TearDown();
    }

    private string TrackTempFile()
    {
        var path = CreateTempAudioFile();
        _tempFilesToCleanup.Add(path);
        return path;
    }

    private TestFSMRef<MainOrchestratorActor, AppState, StateData> CreateOrchestrator(
        IActorPropsFactory propsFactory,
        AppSettings settings,
        RetryPolicySettings retrySettings,
        string actorName)
    {
        return ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    propsFactory,
                    _mockClipboardService,
                    MockWaylandClient,
                    Logger,
                    settings,
                    retrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            actorName);
    }

    private void DriveToReprocessableStateViaCancel(
        TestFSMRef<MainOrchestratorActor, AppState, StateData> orchestrator,
        string tempFile)
    {
        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        orchestrator.Tell(new CancelPipelineCommand());

        AwaitAssert(
            () => orchestrator.StateName.Should().Be(AppState.Idle),
            TimeSpan.FromSeconds(2));

        _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Retain_Audio_File_On_Error_When_KeepLastRecording_Enabled()
    {
        var tempFile = TrackTempFile();
        var propsFactory = new ConfigurableErrorPropsFactory(
                FastDelay, FastDelay, FastDelay, FastDelay, TestScheduler)
            .WithFailingTranscribing();
        var settings = CreateSettingsWithKeepLastRecording();

        var orchestrator = CreateOrchestrator(
            propsFactory, settings, FastRetrySettings, "keep-on-error-enabled");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));

        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(5));

        AwaitAssert(
            () => File.Exists(tempFile).Should().BeTrue("audio file should be retained on error when KeepLastRecording is enabled"),
            TimeSpan.FromSeconds(2));

        var reprocessEvent = _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
        reprocessEvent.IsAvailable.Should().BeTrue();
    }

    [Test]
    public void Should_Delete_Audio_File_On_Error_When_KeepLastRecording_Disabled()
    {
        var tempFile = TrackTempFile();
        var propsFactory = new ConfigurableErrorPropsFactory(
                FastDelay, FastDelay, FastDelay, FastDelay, TestScheduler)
            .WithFailingTranscribing();

        var orchestrator = CreateOrchestrator(
            propsFactory, TestSettings, FastRetrySettings, "delete-on-error-disabled");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));

        _observerProbe.FishForMessage<StateUpdatedEvent>(
            evt => evt.State == AppState.Error,
            max: TimeSpan.FromSeconds(5));

        AwaitAssert(
            () => File.Exists(tempFile).Should().BeFalse("audio file should be deleted on error when KeepLastRecording is disabled"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Retain_Audio_File_After_Success_When_KeepLastRecording_Enabled()
    {
        var tempFile = TrackTempFile();
        var propsFactory = new TestActorPropsFactory(this);
        var settings = CreateSettingsWithKeepLastRecording();

        var orchestrator = CreateOrchestrator(
            propsFactory, settings, TestRetrySettings, "keep-on-success-enabled");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));
        orchestrator.Tell(new TranscriptionCompletedEvent("Hello world"));
        orchestrator.Tell(new ResultSavedEvent("Hello world"));

        AwaitAssert(
            () => orchestrator.StateName.Should().Be(AppState.Idle),
            TimeSpan.FromSeconds(2));

        File.Exists(tempFile).Should().BeTrue("audio file should be retained after success when KeepLastRecording is enabled");

        var reprocessEvent = _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
        reprocessEvent.IsAvailable.Should().BeTrue();
    }

    [Test]
    public void Should_Clear_Reprocessable_Path_On_New_Recording()
    {
        var oldFile = TrackTempFile();
        var propsFactory = new TestActorPropsFactory(this);
        var settings = CreateSettingsWithKeepLastRecording();

        var orchestrator = CreateOrchestrator(
            propsFactory, settings, TestRetrySettings, "clear-on-new-recording");

        DriveToReprocessableStateViaCancel(orchestrator, oldFile);
        File.Exists(oldFile).Should().BeTrue();

        orchestrator.Tell(new ToggleCommand());

        AwaitAssert(
            () => orchestrator.StateName.Should().Be(AppState.Recording),
            TimeSpan.FromSeconds(2));

        var clearedEvent = _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => !evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
        clearedEvent.IsAvailable.Should().BeFalse();

        AwaitAssert(
            () => File.Exists(oldFile).Should().BeFalse("previous retained file should be deleted when a new recording starts"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Clear_Reprocessable_Path_When_Setting_Toggled_Off()
    {
        var tempFile = TrackTempFile();
        var propsFactory = new TestActorPropsFactory(this);
        var settings = CreateSettingsWithKeepLastRecording();

        var orchestrator = CreateOrchestrator(
            propsFactory, settings, TestRetrySettings, "clear-when-setting-off");

        DriveToReprocessableStateViaCancel(orchestrator, tempFile);
        File.Exists(tempFile).Should().BeTrue();

        var updatedSettings = settings with { KeepLastRecording = false };
        orchestrator.Tell(new UpdateSettingsCommand(updatedSettings));

        AwaitAssert(
            () => File.Exists(tempFile).Should().BeFalse("retained file should be deleted when KeepLastRecording is turned off"),
            TimeSpan.FromSeconds(2));

        var clearedEvent = _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => !evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
        clearedEvent.IsAvailable.Should().BeFalse();
    }

    [Test]
    public void Should_Track_AudioFilePath_From_RecordingStartedEvent()
    {
        var tempFile = TrackTempFile();
        var propsFactory = new TestActorPropsFactory(this);

        var orchestrator = CreateOrchestrator(
            propsFactory, TestSettings, TestRetrySettings, "track-recording-started-path");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        orchestrator.Tell(new RecordingStartedEvent(tempFile));

        AwaitAssert(
            () => orchestrator.StateData.LastAudioFilePath.Should().Be(tempFile),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Use_Frozen_KeepLastRecording_On_Cancel_During_Active_Session()
    {
        var tempFile = TrackTempFile();
        var propsFactory = new TestActorPropsFactory(this);

        var orchestrator = CreateOrchestrator(
            propsFactory, TestSettings, TestRetrySettings, "frozen-keep-on-cancel");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        var stashedSettings = CreateSettingsWithKeepLastRecording();
        orchestrator.Tell(new UpdateSettingsCommand(stashedSettings));

        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new CancelPipelineCommand());

        AwaitAssert(
            () => orchestrator.StateName.Should().Be(AppState.Idle),
            TimeSpan.FromSeconds(2));

        AwaitAssert(
            () => File.Exists(tempFile).Should().BeFalse("cancel should use frozen KeepLastRecording=false, not the stashed update"),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Replace_Retained_File_On_New_Recording_Start()
    {
        var fileA = TrackTempFile();
        var fileB = TrackTempFile();
        var propsFactory = new TestActorPropsFactory(this);
        var settings = CreateSettingsWithKeepLastRecording();

        var orchestrator = CreateOrchestrator(
            propsFactory, settings, TestRetrySettings, "replace-retained-file");

        DriveToReprocessableStateViaCancel(orchestrator, fileA);
        File.Exists(fileA).Should().BeTrue();

        orchestrator.Tell(new ToggleCommand());

        AwaitAssert(
            () => File.Exists(fileA).Should().BeFalse("previous retained file A should be deleted when a new recording starts"),
            TimeSpan.FromSeconds(2));

        orchestrator.Tell(new RecordingStartedEvent(fileB));

        AwaitAssert(
            () => orchestrator.StateData.LastAudioFilePath.Should().Be(fileB),
            TimeSpan.FromSeconds(2));
    }
}
