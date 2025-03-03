using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using Avalonia.Controls;
using Avalonia.ReactiveUI;

namespace WhisperVoiceInput.ViewModels
{
    public class AboutWindowViewModel : ViewModelBase
    {
        private readonly Window _window;

        private readonly ObservableAsPropertyHelper<string> _version;
        public string Version => _version.Value;
        

        public AboutWindowViewModel(Window window)
        {
            _window = window;
            CloseCommand = ReactiveCommand.Create(Close, null, AvaloniaScheduler.Instance);

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
            _version = Observable.Return("Version: " + version)
                .ToProperty(this, nameof(Version));
        }

        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private void Close()
        {
            _window.Close();
        }
    }
}