using System;
using System.Linq;
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
public class CompletionHookTests : AkkaTestBase
{
    private TestProbe _observerProbe = null!;
    private MockClipboardService _mockClipboardService = null!;

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
    public void Should_Complete_Pipeline_With_None_OutputType()
    {
        var settings = TestSettings with { OutputType = ResultOutputType.None };
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-none-output"
        );

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        orchestrator.Tell(new ToggleCommand());
        TestScheduler.Advance(_recordingDelay);
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        TestScheduler.Advance(_transcriptionDelay);
        orchestrator.StateName.Should().Be(AppState.Saving);

        TestScheduler.Advance(_savingDelay);
        orchestrator.StateName.Should().Be(AppState.Idle);

        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(1),
            evt => evt is StateUpdatedEvent se ? se : null!,
            10
        );

        stateEvents.Should().HaveCount(6);
        stateEvents[0].State.Should().Be(AppState.Idle);
        stateEvents[1].State.Should().Be(AppState.Recording);
        stateEvents[2].State.Should().Be(AppState.Transcribing);
        stateEvents[3].State.Should().Be(AppState.Saving);
        stateEvents[4].State.Should().Be(AppState.Success);
        stateEvents[5].State.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Complete_Pipeline_With_None_OutputType_And_PostProcessing()
    {
        var settings = TestSettings with
        {
            OutputType = ResultOutputType.None,
            PostProcessingEnabled = true
        };
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-none-output-pp"
        );

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        orchestrator.Tell(new ToggleCommand());
        TestScheduler.Advance(_recordingDelay);
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        TestScheduler.Advance(_transcriptionDelay);
        orchestrator.StateName.Should().Be(AppState.PostProcessing);

        TestScheduler.Advance(_postProcessingDelay);
        orchestrator.StateName.Should().Be(AppState.Saving);

        TestScheduler.Advance(_savingDelay);
        orchestrator.StateName.Should().Be(AppState.Idle);

        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(1),
            evt => evt is StateUpdatedEvent se ? se : null!,
            10
        );

        stateEvents.Should().HaveCount(7);
        stateEvents[0].State.Should().Be(AppState.Idle);
        stateEvents[1].State.Should().Be(AppState.Recording);
        stateEvents[2].State.Should().Be(AppState.Transcribing);
        stateEvents[3].State.Should().Be(AppState.PostProcessing);
        stateEvents[4].State.Should().Be(AppState.Saving);
        stateEvents[5].State.Should().Be(AppState.Success);
        stateEvents[6].State.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Complete_Pipeline_When_Hook_Enabled()
    {
        var settings = CreateSettingsWithCompletionHook("true");
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-hook-enabled"
        );

        orchestrator.Tell(new ToggleCommand());
        orchestrator.StateName.Should().Be(AppState.Recording);

        orchestrator.Tell(new ToggleCommand());
        TestScheduler.Advance(_recordingDelay);
        orchestrator.StateName.Should().Be(AppState.Transcribing);

        TestScheduler.Advance(_transcriptionDelay);
        orchestrator.StateName.Should().Be(AppState.Saving);

        TestScheduler.Advance(_savingDelay);
        orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Complete_Pipeline_When_Hook_Disabled()
    {
        var settings = TestSettings with
        {
            CompletionHookEnabled = false,
            CompletionHookCommand = "notify-send test"
        };
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-hook-disabled"
        );

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new ToggleCommand());
        TestScheduler.Advance(_recordingDelay);
        TestScheduler.Advance(_transcriptionDelay);
        TestScheduler.Advance(_savingDelay);

        orchestrator.StateName.Should().Be(AppState.Idle);
    }

    [Test]
    public void Should_Complete_Pipeline_With_Hook_And_None_OutputType()
    {
        var settings = TestSettings with
        {
            OutputType = ResultOutputType.None,
            CompletionHookEnabled = true,
            CompletionHookCommand = "true"
        };
        var mockPropsFactory = new MockActorPropsFactory(
            _recordingDelay, _transcriptionDelay, _postProcessingDelay, _savingDelay, TestScheduler);

        var orchestrator = ActorOfAsTestFSMRef<MainOrchestratorActor, AppState, StateData>(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    Logger,
                    settings,
                    TestRetrySettings,
                    _observerProbe.Ref))
                .WithDispatcher(CallingThreadDispatcher.Id),
            "test-orchestrator-hook-none"
        );

        orchestrator.Tell(new ToggleCommand());
        orchestrator.Tell(new ToggleCommand());
        TestScheduler.Advance(_recordingDelay);
        TestScheduler.Advance(_transcriptionDelay);
        TestScheduler.Advance(_savingDelay);

        orchestrator.StateName.Should().Be(AppState.Idle);

        var stateEvents = _observerProbe.ReceiveWhile<StateUpdatedEvent>(
            TimeSpan.FromSeconds(1),
            evt => evt is StateUpdatedEvent se ? se : null!,
            10
        );

        stateEvents.Should().HaveCount(6);
        stateEvents[4].State.Should().Be(AppState.Success);
    }
}
