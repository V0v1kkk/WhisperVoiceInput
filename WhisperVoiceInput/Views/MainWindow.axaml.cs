using Avalonia.Controls;

namespace WhisperVoiceInput.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        
        var clipboard = this.Clipboard;
        
        clipboard?.SetTextAsync("Hello, World - test test test!").GetAwaiter().GetResult();
    }
}