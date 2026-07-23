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
public class ActorNamingTests : AkkaTestBase
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

    [Test]
    public void Should_Not_Throw_On_Rapid_Cancel_Then_Reprocess()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "rapid-cancel-reprocess-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        Action rapidCancelAndReprocess = () =>
        {
            orchestrator.Tell(new CancelPipelineCommand());
            orchestrator.Tell(new ReprocessCommand());
        };

        rapidCancelAndReprocess.Should().NotThrow();

        orchestrator.StateName.Should().Be(AppState.Transcribing);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();
    }

    [Test]
    public void Should_Not_Throw_On_Rapid_Cancel_Then_Toggle()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "rapid-cancel-toggle-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new AudioRecordedEvent("test.wav"));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        Action rapidCancelAndToggle = () =>
        {
            orchestrator.Tell(new CancelPipelineCommand());
            orchestrator.Tell(new ToggleCommand());
        };

        rapidCancelAndToggle.Should().NotThrow();

        orchestrator.StateName.Should().Be(AppState.Recording);
        propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();
    }

    [Test]
    public void Should_Succeed_On_Multiple_Cancel_And_Restart_Cycles()
    {
        var propsFactory = new TestActorPropsFactory(this);
        var orchestrator = CreateOrchestrator(propsFactory, TestSettings, "multiple-cancel-restart-test");

        for (var cycle = 1; cycle <= 5; cycle++)
        {
            orchestrator.Tell(new ToggleCommand());
            propsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();
            orchestrator.StateName.Should().Be(AppState.Recording);

            orchestrator.Tell(new AudioRecordedEvent($"cycle-{cycle}.wav"));
            propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();
            orchestrator.StateName.Should().Be(AppState.Transcribing);
            orchestrator.StateData.SessionId.Should().NotBe(default(Guid));

            orchestrator.Tell(new CancelPipelineCommand());
            _observerProbe.FishForMessage<StateUpdatedEvent>(
                evt => evt.State == AppState.Idle,
                max: TimeSpan.FromSeconds(2));
            orchestrator.StateName.Should().Be(AppState.Idle);
        }
    }

    [Test]
    public void Should_Succeed_On_Cancel_Reprocess_Cancel_Reprocess_Chain()
    {
        var settings = CreateSettingsWithKeepLastRecording();
        var propsFactory = new TestActorPropsFactory(this);
        var tempFile = TrackTempFile();
        var orchestrator = CreateOrchestrator(propsFactory, settings, "cancel-reprocess-chain-test");

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new RecordingStartedEvent(tempFile));
        orchestrator.Tell(new AudioRecordedEvent(tempFile));
        orchestrator.StateName.Should().Be(AppState.Transcribing);
        propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        var previousSessionId = orchestrator.StateData.SessionId;
        for (var chain = 1; chain <= 3; chain++)
        {
            orchestrator.Tell(new CancelPipelineCommand());
            orchestrator.StateName.Should().Be(AppState.Idle);

            orchestrator.Tell(new ReprocessCommand());
            propsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>(TimeSpan.FromSeconds(3));
            orchestrator.StateName.Should().Be(AppState.Transcribing);
            orchestrator.StateData.SessionId.Should().NotBe(previousSessionId);
            previousSessionId = orchestrator.StateData.SessionId;
        }
    }
}
