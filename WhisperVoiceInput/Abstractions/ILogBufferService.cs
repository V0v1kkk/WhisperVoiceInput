using System;
using DynamicData;
using Serilog.Events;

namespace WhisperVoiceInput.Abstractions;

public interface ILogBufferService
{
    int Capacity { get; }
    void UpdateCapacity(int newCapacity);

    IObservable<IChangeSet<LogRecord>> Connect();
}

public sealed record LogRecord(LogEvent LogEvent, string Formatted);




