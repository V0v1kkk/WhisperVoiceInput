using Avalonia.Controls;

namespace WhisperVoiceInput.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        // Prevent the window from closing
        e.Cancel = true;

        // Hide the window instead
        Hide();
    }
}