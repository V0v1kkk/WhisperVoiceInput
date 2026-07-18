using System;
using System.Collections.Generic;
using System.IO;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Tests.TestBase;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Actors;

[TestFixture]
public class StaleSessionEventTests : AkkaTestBase
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

    private string TrackTempFile()
    {
        var path = CreateTempAudioFile();
        _tempFiles.Add(path);
        return path;
    }

    private static void DriveToTranscribing(
        TestFSMRef<MainOrchestratorActor, AppState, StateData> orchestrator,
        string audioFilePath)
    {
        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(audioFilePath));
        orchestrator.Tell(new AudioRecordedEvent(audioFilePath));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
    }

    private void CancelAndReprocess(
        TestFSMRef<MainOrchestratorActor, AppState, StateData> orchestrator,
        TestActorPropsFactory propsFactory)
    {
        orchestrator.Tell(new CancelPipelineCommand());
        orchestrator.StateName.Should().Be(AppState.Idle);

        // Drain observer queue to avoid FishForMessage timeout on repeat cycles
        _observerProbe.ReceiveWhile<object>(TimeSpan.FromMilliseconds(500), msg => msg, 20);

        orchestrator.Tell(new ReprocessCommand());
        var cmd = propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(2));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
    }

    [Test]
    public void Should_Ignore_Stale_TranscriptionCompletedEvent_After_Cancel_And_Reprocess()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "stale-transcription-test");

        DriveToTranscribing(orchestrator, tempFile);
        var session1Id = orchestrator.StateData.SessionId;
        session1Id.Should().NotBe(default(Guid));
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        CancelAndReprocess(orchestrator, propsFactory);
        orchestrator.StateData.SessionId.Should().NotBe(session1Id);

        orchestrator.Tell(new TranscriptionCompletedEvent("stale text", SessionId: session1Id));

        orchestrator.StateName.Should().Be(AppState.Transcribing,
            "stale session-1 transcription result must not advance the session-2 pipeline");

        orchestrator.Tell(new TranscriptionCompletedEvent("session 2 text"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>(evt => evt.Text == "session 2 text");
    }

    [Test]
    public void Should_Ignore_Stale_PostProcessedEvent_After_Cancel_And_Reprocess()
    {
        var settings = CreateSettingsWithKeepLastRecording(withPostProcessing: true);
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "stale-postprocessed-test");

        DriveToTranscribing(orchestrator, tempFile);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();
        orchestrator.Tell(new TranscriptionCompletedEvent("raw text"));
        orchestrator.StateName.Should().Be(AppState.PostProcessing);
        propsFactory.PostProcessorProbe.ExpectMsg<PostProcessCommand>();

        CancelAndReprocess(orchestrator, propsFactory);

        orchestrator.Tell(new TranscriptionCompletedEvent("session 2 raw"));
        orchestrator.StateName.Should().Be(AppState.PostProcessing);
        propsFactory.PostProcessorProbe.ExpectMsg<PostProcessCommand>();

        var staleSessionId = Guid.NewGuid();
        orchestrator.Tell(new PostProcessedEvent("stale processed", SessionId: staleSessionId));
        orchestrator.StateName.Should().Be(AppState.PostProcessing,
            "stale session-1 post-processing result must not advance the session-2 pipeline");

        orchestrator.Tell(new PostProcessedEvent("session 2 processed"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>(evt => evt.Text == "session 2 processed");
    }

    [Test]
    public void Should_Ignore_Stale_ResultSavedEvent_After_Cancel_During_Saving()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "stale-resultsaved-test");

        DriveToTranscribing(orchestrator, tempFile);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();
        orchestrator.Tell(new TranscriptionCompletedEvent("original text"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>();

        CancelAndReprocess(orchestrator, propsFactory);

        orchestrator.Tell(new TranscriptionCompletedEvent("reprocessed text"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>(evt => evt.Text == "reprocessed text");

        var staleSessionId = Guid.NewGuid();
        orchestrator.Tell(new ResultSavedEvent("stale saved", SessionId: staleSessionId));
        orchestrator.StateName.Should().Be(AppState.Saving,
            "stale session-1 result-saved event must not complete the session-2 pipeline");

        orchestrator.Tell(new ResultSavedEvent("session 2 saved"));
        orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Process_Events_With_Matching_SessionId_During_Normal_Pipeline()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "normal-pipeline-session-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new AudioRecordedEvent("test.wav"));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        orchestrator.StateData.SessionId.Should().NotBe(default(Guid));

        orchestrator.Tell(new TranscriptionCompletedEvent("hello world"));
        orchestrator.StateName.Should().Be(AppState.Saving);
        propsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>();

        orchestrator.Tell(new ResultSavedEvent("hello world"));
        orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Increment_SessionId_On_Each_Cancel_And_Reprocess_Cycle()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "session-id-increment-test");

        DriveToTranscribing(orchestrator, tempFile);
        var previousSessionId = orchestrator.StateData.SessionId;
        previousSessionId.Should().NotBe(default(Guid));
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            CancelAndReprocess(orchestrator, propsFactory);
            var currentSessionId = orchestrator.StateData.SessionId;
            currentSessionId.Should().NotBe(previousSessionId,
                $"session ID must change on cycle {cycle}");
            previousSessionId = currentSessionId;
        }
    }
}
