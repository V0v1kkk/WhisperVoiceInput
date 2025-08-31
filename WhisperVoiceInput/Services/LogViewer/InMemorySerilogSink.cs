using Serilog.Core;
using Serilog.Events;

namespace WhisperVoiceInput.Services.LogViewer;

public sealed class InMemorySerilogSink(InMemoryLogBufferService buffer) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        buffer.Add(logEvent);
    }
}




