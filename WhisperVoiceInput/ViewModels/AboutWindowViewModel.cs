using System.Reactive;
using ReactiveUI;
using Avalonia.Controls;
using Avalonia.ReactiveUI;

namespace WhisperVoiceInput.ViewModels
{
    public class AboutWindowViewModel : ViewModelBase
    {
        private readonly Window _window;

        public AboutWindowViewModel(Window window)
        {
            _window = window;
            CloseCommand = ReactiveCommand.Create(Close, null, AvaloniaScheduler.Instance);
        }

        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private void Close()
        {
            _window.Close();
        }
    }
}