using DynamicData;
using FluentAssertions;
using NUnit.Framework;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Formatting.Display;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Services.LogViewer;

namespace WhisperVoiceInput.Tests.Services;

[TestFixture]
public class InMemoryLogBufferServiceTests
{
    private static LogEvent CreateEvent(string msg, LogEventLevel level = LogEventLevel.Information)
    {
        var parser = new MessageTemplateParser();
        var template = parser.Parse(msg);
        return new LogEvent(DateTimeOffset.Now, level, null, template, new List<LogEventProperty>());
    }

    [Test]
    public void Add_EvictsBeyondCapacity()
    {
        var formatter = new MessageTemplateTextFormatter("{Message}");
        var svc = new InMemoryLogBufferService(formatter, 2);
        var seen = new List<IChangeSet<LogRecord>>();
        using var sub = svc.Connect().Subscribe(seen.Add);

        svc.Add(CreateEvent("a"));
        svc.Add(CreateEvent("b"));
        seen.Clear();
        svc.Add(CreateEvent("c"));

        var flat = seen.SelectMany(c => c).ToList();
        flat.Should().ContainSingle(c => c.Reason == ListChangeReason.Remove);
        flat.Should().ContainSingle(c => c.Reason == ListChangeReason.Add);
    }
}


