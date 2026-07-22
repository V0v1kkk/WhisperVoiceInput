using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using WhisperVoiceInput.Services;
using WhisperVoiceInput.Tests.TestDoubles;

namespace WhisperVoiceInput.Tests.Services;

[TestFixture]
public class ClipboardServiceTests
{
    private CollectingSink _sink = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _sink = new CollectingSink();
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(_sink)
            .CreateLogger();
    }

    [TearDown]
    public void TearDown()
    {
        (_logger as IDisposable)?.Dispose();
    }

    [Test]
    public async Task SetTextAsync_WhenPlatformClipboardIsNull_ThrowsInvalidOperationException()
    {
        var service = new ClipboardService(_logger, () => null);

        var act = () => service.SetTextAsync("hello");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Clipboard not available*");
        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Error &&
            e.MessageTemplate.Text.Contains("Platform clipboard service is not available"));
    }

    [Test]
    public async Task SetTextAsync_WhenPlatformClipboardThrows_LogsAndRethrows()
    {
        var (clipboard, mock) = TestClipboardFactory.CreateThrowing(
            new PlatformNotSupportedException("Wayland clipboard unavailable"));
        var service = new ClipboardService(_logger, () => clipboard);

        var act = () => service.SetTextAsync("hello");

        await act.Should().ThrowAsync<PlatformNotSupportedException>()
            .WithMessage("Wayland clipboard unavailable");
        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Error &&
            e.MessageTemplate.Text.Contains("Failed to copy text to clipboard"));
        mock.Verify(c => c.SetDataAsync(It.IsAny<Avalonia.Input.IAsyncDataTransfer?>()), Times.Once);
    }

    [Test]
    public async Task SetTextAsync_WhenPlatformClipboardAvailable_CopiesText()
    {
        var (clipboard, mock) = TestClipboardFactory.Create();
        var service = new ClipboardService(_logger, () => clipboard);

        await service.SetTextAsync("transcribed text");

        mock.Verify(c => c.SetDataAsync(It.IsAny<Avalonia.Input.IAsyncDataTransfer?>()), Times.Once);
        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Debug &&
            e.MessageTemplate.Text.Contains("Text copied to clipboard successfully"));
    }

    [Test]
    public void SetTopLevel_WithNullTopLevel_ThrowsArgumentNullException()
    {
        var service = new ClipboardService(_logger, () => null);

        var act = () => service.SetTopLevel(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [AvaloniaTest]
    public async Task SetTopLevel_WithHeadlessWindow_CopiesTextWithoutUsingPlatformResolver()
    {
        var (platformClipboard, platformMock) = TestClipboardFactory.Create();
        var service = new ClipboardService(_logger, () => platformClipboard);

        var window = new Window();
        service.SetTopLevel(window);

        await service.SetTextAsync("from window");

        platformMock.Verify(
            c => c.SetDataAsync(It.IsAny<Avalonia.Input.IAsyncDataTransfer?>()),
            Times.Never,
            "TopLevel clipboard should be used instead of platform resolver");
        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Debug &&
            e.MessageTemplate.Text.Contains("Text copied to clipboard successfully"));
    }

    [Test]
    public void SetTextAsync_WithNullText_ThrowsArgumentNullException()
    {
        var (clipboard, _) = TestClipboardFactory.Create();
        var service = new ClipboardService(_logger, () => clipboard);

        var act = () => service.SetTextAsync(null!);

        act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
