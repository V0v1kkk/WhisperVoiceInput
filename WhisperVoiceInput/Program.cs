using Avalonia;
using System;
using Avalonia.ReactiveUI;

namespace WhisperVoiceInput;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                WmClass = "WhisperVoiceInput",
                EnableSessionManagement = false, // Disable to avoid blocking shutdown on logout
            })
            .WithInterFont()
            .UseReactiveUI()
            .LogToTrace();
}
