using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Actors;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Tests.TestBase;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Actors;

[TestFixture]
public class ResultSaverOutputTests : AkkaTestBase
{
    private MockClipboardService _mockClipboardService = null!;
    private MockWaylandInputMethodClient _waylandClient = null!;

    public override void Setup()
    {
        base.Setup();
        _mockClipboardService = new MockClipboardService();
        _waylandClient = new MockWaylandInputMethodClient();
    }

    private IActorRef CreateResultSaverUnderProbe(TestProbe parentProbe, AppSettings settings,
        IClipboardService? clipboardService = null)
    {
        return parentProbe.ChildActorOf(
            Props.Create(() => new ResultSaverActor(
                settings,
                Logger,
                clipboardService ?? _mockClipboardService,
                _waylandClient)));
    }

    [Test]
    public void ClipboardAvaloniaApi_CopiesTextViaClipboardService()
    {
        var parentProbe = CreateTestProbe();
        var settings = TestSettings with { OutputType = ResultOutputType.ClipboardAvaloniaApi };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("clipboard text"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "clipboard text");
        _mockClipboardService.SetTextCallCount.Should().Be(1);
        _mockClipboardService.LastText.Should().Be("clipboard text");
    }

    [Test]
    public void ClipboardAvaloniaApi_WhenClipboardFails_RestartsActorForRetry()
    {
        var failingClipboard = new FailingClipboardService();
        var parentProbe = CreateTestProbe();
        var settings = TestSettings with { OutputType = ResultOutputType.ClipboardAvaloniaApi };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings, failingClipboard);

        actor.Tell(new ResultAvailableEvent("will fail"));

        parentProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        Sys.Stop(actor);
    }

    [Test]
    public void NoneOutputType_SkipsClipboardWithoutError()
    {
        var parentProbe = CreateTestProbe();
        var settings = TestSettings with { OutputType = ResultOutputType.None };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("ignored text"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "ignored text");
        _mockClipboardService.SetTextCallCount.Should().Be(0);
    }

    [Test]
    public void UnknownOutputType_SkipsClipboardWithoutError()
    {
        var parentProbe = CreateTestProbe();
        var settings = TestSettings with { OutputType = (ResultOutputType)999 };
        var actor = CreateResultSaverUnderProbe(parentProbe, settings);

        actor.Tell(new ResultAvailableEvent("unknown mode"));

        parentProbe.ExpectMsg<ResultSavedEvent>(msg => msg.Text == "unknown mode");
        _mockClipboardService.SetTextCallCount.Should().Be(0);
    }
}
