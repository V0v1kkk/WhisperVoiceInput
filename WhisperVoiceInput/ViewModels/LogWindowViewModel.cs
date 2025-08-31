using System;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Serilog.Events;
using WhisperVoiceInput.Abstractions;
using System.Reactive.Linq;

namespace WhisperVoiceInput.ViewModels;

public sealed class LogWindowViewModel : ReactiveObject, IDisposable
{
    private readonly ReadOnlyObservableCollection<LogRecord> _items;
    private readonly IDisposable _subscription;

    public ReadOnlyObservableCollection<LogRecord> Items => _items;

    public LogEventLevel[] Levels { get; } =
        Enum.GetValues(typeof(LogEventLevel)).Cast<LogEventLevel>().ToArray();

    private LogEventLevel _selectedLevel = LogEventLevel.Information;
    public LogEventLevel SelectedLevel
    {
        get => _selectedLevel;
        set => this.RaiseAndSetIfChanged(ref _selectedLevel, value);
    }

    private bool _autoScrollEnabled = true;
    public bool AutoScrollEnabled
    {
        get => _autoScrollEnabled;
        set => this.RaiseAndSetIfChanged(ref _autoScrollEnabled, value);
    }

#pragma warning disable CS8618
    public LogWindowViewModel()

    {
        // design-time
    }
#pragma warning restore CS8618

    public LogWindowViewModel(ILogBufferService buffer)
    {
        _subscription = buffer.Connect()
            .Filter(this.WhenAnyValue(x => x.SelectedLevel)
                .Select(l => (Func<LogRecord, bool>)(r => r.LogEvent.Level >= l)))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _items)
            .Subscribe();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}




