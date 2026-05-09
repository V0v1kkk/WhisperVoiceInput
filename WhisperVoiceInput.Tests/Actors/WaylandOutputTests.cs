using Akka.Actor;
using Akka.TestKit;
using NUnit.Framework;
using Serilog;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Tests.TestBase;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Actors;

[TestFixture]
public class WaylandOutputTests : AkkaTestBase
{
    private MockClipboardService _mockClipboardService = null!;
    private MockWaylandInputMethodClient _waylandClient = null!;

    public override void Setup()
    {
        base.Setup();
        _mockClipboardService = new MockClipboardService();
        _waylandClient = new MockWaylandInputMethodClient();
    }

    private IActorRef CreateResultSaverUnderProbe(TestProbe parentProbe, AppSettings settings)
    {
        return parentProbe.ChildActorOf(
            Props.Create(() => new ResultSaverActor(settings, Logger, _mockClipboardService, _waylandClient)));
    }

    [Test]
    public void ResultSaverActor_WaylandInputMethod_CommitsViaWayland_WhenCommitSucceeds()
    {
        _waylandClient.CommitResult = true;

        var parentProbe = CreateTestProbe();
        var settings = TestSettings with { OutputType = ResultOutputType.WaylandInputMethod };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("Hello from test"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "Hello from test");
        Assert.That(_waylandClient.LastCommittedText, Is.EqualTo("Hello from test"));
        Assert.That(_waylandClient.CommitCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ResultSaverActor_WaylandInputMethod_FallsBackToClipboard_WhenCommitFails()
    {
        _waylandClient.CommitResult = false;

        var parentProbe = CreateTestProbe();
        var settings = TestSettings with
        {
            OutputType = ResultOutputType.WaylandInputMethod,
            WaylandImeFallbackType = ResultOutputType.ClipboardAvaloniaApi
        };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("Fallback text"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "Fallback text");
        Assert.That(_waylandClient.CommitCallCount, Is.EqualTo(1));
        Assert.That(_mockClipboardService.SetTextCallCount, Is.EqualTo(1));
        Assert.That(_mockClipboardService.LastText, Is.EqualTo("Fallback text"));
    }

    [Test]
    public void ResultSaverActor_WaylandInputMethod_UsesDefaultFallback_WhenFallbackIsWaylandInputMethod()
    {
        _waylandClient.CommitResult = false;

        var parentProbe = CreateTestProbe();
        var settings = TestSettings with
        {
            OutputType = ResultOutputType.WaylandInputMethod,
            WaylandImeFallbackType = ResultOutputType.WaylandInputMethod
        };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("Guard text"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "Guard text");
        Assert.That(_waylandClient.CommitCallCount, Is.EqualTo(1));
        Assert.That(_mockClipboardService.SetTextCallCount, Is.EqualTo(0),
            "Recursion guard: WaylandInputMethod fallback is silently skipped");
    }

    [Test]
    public void ResultSaverActor_WaylandInputMethod_SkipsOutput_WhenFallbackIsNone()
    {
        _waylandClient.CommitResult = false;

        var parentProbe = CreateTestProbe();
        var settings = TestSettings with
        {
            OutputType = ResultOutputType.WaylandInputMethod,
            WaylandImeFallbackType = ResultOutputType.None
        };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("None guard text"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "None guard text");
        Assert.That(_waylandClient.CommitCallCount, Is.EqualTo(1));
        Assert.That(_mockClipboardService.SetTextCallCount, Is.EqualTo(0),
            "None fallback means do nothing");
    }

    [Test]
    public void FullPipeline_WaylandInputMethod_DeliversTextToResultSaver()
    {
        _waylandClient.CommitResult = true;

        var mockPropsFactory = new TestActorPropsFactory(this);
        var settings = TestSettings with { OutputType = ResultOutputType.WaylandInputMethod };

        var orchestrator = Sys.ActorOf(
            Props.Create(() => new MainOrchestratorActor(
                    mockPropsFactory,
                    _mockClipboardService,
                    _waylandClient,
                    Logger,
                    settings,
                    TestRetrySettings,
                    mockPropsFactory.ObserverProbe.Ref)),
                "orchestrator-wayland");

        orchestrator.Tell(new ToggleCommand());
        mockPropsFactory.AudioRecordingProbe.ExpectMsg<RecordCommand>();

        orchestrator.Tell(new ToggleCommand());
        mockPropsFactory.AudioRecordingProbe.ExpectMsg<StopRecordingCommand>();

        orchestrator.Tell(new AudioRecordedEvent("/tmp/test.wav"));
        mockPropsFactory.TranscribingProbe.ExpectMsg<TranscribeCommand>();

        orchestrator.Tell(new TranscriptionCompletedEvent("Wayland test text"));

        mockPropsFactory.ResultSaverProbe.ExpectMsg<ResultAvailableEvent>(msg =>
            msg.Text == "Wayland test text");
    }
}
