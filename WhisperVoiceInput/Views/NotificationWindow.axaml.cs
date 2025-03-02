using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WhisperVoiceInput.Views
{
    public partial class NotificationWindow : Window
    {
        public NotificationWindow()
        {
            InitializeComponent();
            
            // Position the window in the bottom right corner of the screen
            PositionWindowBottomRight();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void Window_Closing(object? sender, WindowClosingEventArgs e)
        {
            // Prevent the window from closing
            e.Cancel = true;

            // Hide the window instead
            Hide();
        }

        private void PositionWindowBottomRight()
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                
                // Position the window in the bottom right corner with a small margin
                Position = new PixelPoint(
                    (int)(workingArea.Right - Width - 20),
                    (int)(workingArea.Bottom - Height - 20)
                );
            }
        }
    }
}