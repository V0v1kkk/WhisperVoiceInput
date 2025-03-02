using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Serilog;

namespace WhisperVoiceInput.ViewModels
{
    public class NotificationWindowViewModel : ViewModelBase
    {
        private readonly ILogger _logger;
        private readonly CompositeDisposable _disposables = new();
        
        private readonly ObservableAsPropertyHelper<string> _animatedText;

        public string NotificationText => _animatedText.Value;

#pragma warning disable CS8618, CS9264
        // ReSharper disable once UnusedMember.Global
        public NotificationWindowViewModel() // For design-time data
        {
            _animatedText = Observable.Return("WhisperVoiceInput is starting...")
                .ToProperty(this, nameof(NotificationText));
        }
#pragma warning restore CS8618, CS9264

        public NotificationWindowViewModel(Window window, ILogger logger)
        {
            _logger = logger.ForContext<NotificationWindowViewModel>();
            
            const string notificationText = "WhisperVoiceInput is starting";
            
            var dotAnimation = Observable
                .Timer(TimeSpan.Zero, TimeSpan.FromMilliseconds(450))
                .Take(5)
                .Select(i => i % 4)
                .Select(i => i switch
                {
                    0 => string.Empty,
                    1 => ".",
                    2 => "..",
                    _ => "..."
                })
                .ObserveOn(RxApp.MainThreadScheduler);
            
            // Subscribe to the animation to hide the window when it completes
            dotAnimation
                .TakeLast(1)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(_ =>
                {
                    window.Hide();
                    _logger.Information("Notification window animation completed");
                })
                .DisposeWith(_disposables);
            
            // Convert the animation to a string property
            _animatedText = dotAnimation
                .Select(dots => notificationText + dots)
                .ToProperty(this, nameof(NotificationText))
                .DisposeWith(_disposables);
        }

        public override void Dispose()
        {
            _disposables.Dispose();
            base.Dispose();
        }
    }
}