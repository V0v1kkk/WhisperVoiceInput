using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WhisperVoiceInput.ViewModels;

namespace WhisperVoiceInput.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            DataContext = new AboutWindowViewModel(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}