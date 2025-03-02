using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Serilog;

namespace WhisperVoiceInput.ViewModels
{
    public class NotificationWindowViewModel : ViewModelBase
    {
        private readonly Window _window;
        private readonly ILogger _logger;
        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        
        private string _notificationText = "WhisperVoiceInput is starting";
        private readonly ObservableAsPropertyHelper<string> _countdown;

        public string NotificationText
        {
            get => _notificationText;
            private set => this.RaiseAndSetIfChanged(ref _notificationText, value);
        }

        public string CountdownText => _countdown.Value;

        public NotificationWindowViewModel(Window window, ILogger logger)
        {
            _window = window;
            _logger = logger.ForContext<NotificationWindowViewModel>();
            
            const int countdownSeconds = 3;
            
            // Create a countdown observable that emits values every second
            var countdown = Observable
                .Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1))
                .Take(countdownSeconds + 1) // 0, 1, 2, 3 seconds
                .Select(i => countdownSeconds - i) // Convert to 3, 2, 1, 0
                .ObserveOn(RxApp.MainThreadScheduler);
            
            // Subscribe to the countdown to hide the window when it reaches 0
            countdown
                .Where(i => i == 0)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(_ =>
                {
                    _window.Hide();
                    _logger.Information("Notification window countdown completed");
                })
                .DisposeWith(_disposables);
            
            // Convert the countdown to a string property
            _countdown = countdown
                .Select(i => i.ToString())
                .ToProperty(this, nameof(CountdownText), "3")
                .DisposeWith(_disposables);
        }

        public override void Dispose()
        {
            _disposables.Dispose();
            base.Dispose();
        }
    }
}