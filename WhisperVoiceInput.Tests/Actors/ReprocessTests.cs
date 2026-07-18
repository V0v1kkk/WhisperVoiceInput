using System;
using System.Collections.Generic;
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
public class ReprocessTests : AkkaTestBase
{
    private TestProbe _observerProbe = null!;
    private MockClipboardService _mockClipboardService = null!;
    private readonly List<string> _tempFiles = [];

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _observerProbe = CreateTestProbe();
        _mockClipboardService = new MockClipboardService();
    }

    [TearDown]
    public override void TearDown()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        _tempFiles.Clear();
        base.TearDown();
    }

    private TestFSMRef<MainOrchestratorActor, AppState, StateData> CreateOrchestrator(
        AppSettings settings,
        TestActorPropsFactory propsFactory,
        string name)
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
            name);
    }

    private string TrackTempFile()
    {
        var path = CreateTempAudioFile();
        _tempFiles.Add(path);
        return path;
    }

    private void SetupReprocessableStateViaCancel(
        TestFSMRef<MainOrchestratorActor, AppState, StateData> orchestrator,
        TestActorPropsFactory propsFactory,
        string tempFile)
    {
        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));

        orchestrator.StateName.Should().Be(AppState.Transcribing);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));

        orchestrator.Tell(new CancelPipelineCommand());
        orchestrator.StateName.Should().Be(AppState.Idle);

        _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
    }

    private void ExpectReprocessUnavailable()
    {
        _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => !evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
    }

    private void ExpectReprocessAvailable()
    {
        _observerProbe.FishForMessage<ReprocessAvailableEvent>(
            evt => evt.IsAvailable,
            max: TimeSpan.FromSeconds(2));
    }

    [Test]
    public void Should_Start_Reprocess_From_Transcribing_When_Audio_File_Exists()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-start-from-transcribing");

        SetupReprocessableStateViaCancel(orchestrator, propsFactory, tempFile);

        orchestrator.Tell(new ReprocessCommand());

        var transcribeCommand = propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        transcribeCommand.AudioFile.Should().Be(tempFile);
    }

    [Test]
    public void Should_Stay_Idle_When_Reprocess_With_No_Audio_File()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-no-audio-file");

        orchestrator.StateName.Should().Be(AppState.Idle);

        orchestrator.Tell(new ReprocessCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        ExpectReprocessUnavailable();
    }

    [Test]
    public void Should_Stay_Idle_When_Reprocess_With_Deleted_Audio_File()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-deleted-audio-file");

        SetupReprocessableStateViaCancel(orchestrator, propsFactory, tempFile);

        File.Delete(tempFile);

        orchestrator.Tell(new ReprocessCommand());

        orchestrator.StateName.Should().Be(AppState.Idle);
        ExpectReprocessUnavailable();
    }

    [Test]
    public void Should_Complete_Full_Reprocess_Pipeline_Without_PostProcessing()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-full-no-pp");

        SetupReprocessableStateViaCancel(orchestrator, propsFactory, tempFile);

        orchestrator.Tell(new ReprocessCommand());
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        orchestrator.Tell(new TranscriptionCompletedEvent("reprocessed text"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>();

        orchestrator.Tell(new ResultSavedEvent("reprocessed text"));
        orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Complete_Full_Reprocess_Pipeline_With_PostProcessing()
    {
        var settings = CreateSettingsWithKeepLastRecording(withPostProcessing: true);
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-full-with-pp");

        SetupReprocessableStateViaCancel(orchestrator, propsFactory, tempFile);

        orchestrator.Tell(new ReprocessCommand());
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        orchestrator.Tell(new TranscriptionCompletedEvent("raw text"));
        orchestrator.StateName.Should().Be(AppState.PostProcessing);
        propsFactory.PostProcessorProbe.ExpectMsg<PostProcessCommand>();

        orchestrator.Tell(new PostProcessedEvent("processed text"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>();

        orchestrator.Tell(new ResultSavedEvent("processed text"));
        orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Ignore_Reprocess_During_Active_Pipeline()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-during-active-pipeline");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);
        propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();

        orchestrator.Tell(new ReprocessCommand());

        orchestrator.StateName.Should().Be(AppState.Recording);
    }

    [Test]
    public void Should_Reprocess_After_Cancel()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-after-cancel");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));

        orchestrator.Tell(new CancelPipelineCommand());
        orchestrator.StateName.Should().Be(AppState.Idle);
        ExpectReprocessAvailable();

        orchestrator.Tell(new ReprocessCommand());
        var transcribeCommand = propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        transcribeCommand.AudioFile.Should().Be(tempFile);
    }

    [Test]
    public void Should_Allow_Second_Reprocess_After_Successful_Reprocess()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-second-after-success");

        SetupReprocessableStateViaCancel(orchestrator, propsFactory, tempFile);

        orchestrator.Tell(new ReprocessCommand());
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.Tell(new TranscriptionCompletedEvent("first reprocess"));
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>();
        orchestrator.Tell(new ResultSavedEvent("first reprocess"));
        orchestrator.StateName.Should().Be(AppState.Idle);
        ExpectReprocessAvailable();

        orchestrator.Tell(new ReprocessCommand());
        var secondTranscribe = propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        secondTranscribe.AudioFile.Should().Be(tempFile);
    }

    [Test]
    public void Should_Allow_Reprocess_After_Failed_Reprocess()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(settings, propsFactory, "reprocess-after-failed-reprocess");

        SetupReprocessableStateViaCancel(orchestrator, propsFactory, tempFile);

        orchestrator.Tell(new ReprocessCommand());
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));

        var sessionId = orchestrator.StateData.SessionId;
        var transcribingChild = Sys.ActorSelection(orchestrator.Path / $"transcribing-{sessionId}")
            .ResolveOne(TimeSpan.FromSeconds(2))
            .GetAwaiter().GetResult();
        transcribingChild.Tell(PoisonPill.Instance);

        AwaitAssert(
            () => orchestrator.StateName.Should().Be(AppState.Idle),
            TimeSpan.FromSeconds(2));
        ExpectReprocessAvailable();

        orchestrator.Tell(new ReprocessCommand());
        var transcribeCommand = propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        transcribeCommand.AudioFile.Should().Be(tempFile);

        orchestrator.Tell(new TranscriptionCompletedEvent("recovered text"));
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>();
        orchestrator.Tell(new ResultSavedEvent("recovered text"));
        orchestrator.StateName.Should().Be(AppState.Idle);
    }
}
