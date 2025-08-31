using System;
using System.Threading;
using DynamicData;
using Serilog.Events;
using Serilog.Formatting;
using WhisperVoiceInput.Abstractions;

namespace WhisperVoiceInput.Services.LogViewer;

public sealed class InMemoryLogBufferService : ILogBufferService
{
    private readonly Lock _sync = new();
    private readonly RingSourceList<LogRecord> _list;
    private readonly ITextFormatter _formatter;
    private int _capacity;

    public InMemoryLogBufferService(ITextFormatter formatter, int capacity)
    {
        _formatter = formatter;
        _capacity = capacity > 0 ? capacity : 100;
        _list = new RingSourceList<LogRecord>(_capacity);
    }

    public int Capacity => _capacity;

    public void UpdateCapacity(int newCapacity)
    {
        if (newCapacity <= 0) return;
        lock (_sync)
        {
            if (newCapacity == _capacity) return;
            _list.UpdateCapacity(newCapacity);
            _capacity = newCapacity;
        }
    }

    public IObservable<IChangeSet<LogRecord>> Connect()
    {
        return _list.Connect();
    }

    public void Add(LogEvent? logEvent)
    {
        if (logEvent is null) 
            return;
        
        string formatted;
        using (var sw = new System.IO.StringWriter())
        {
            _formatter.Format(logEvent, sw);
            formatted = sw.ToString();
        }
        var record = new LogRecord(logEvent, formatted);
        lock (_sync)
        {
            _list.Edit(editor => editor.Add(record));
        }
    }
}




