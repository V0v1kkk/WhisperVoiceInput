using Avalonia;
using System;
using System.IO;
using Avalonia.ReactiveUI;
using OpenTK.Audio.OpenAL;

namespace WhisperVoiceInput;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureOpenALForMacOS();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// On macOS, configures OpenTK to use OpenAL Soft instead of Apple's deprecated OpenAL framework.
    /// Apple deprecated OpenAL in macOS 10.15 Catalina, and the built-in version has known issues
    /// with audio capture. OpenAL Soft (installed via Homebrew) provides better capture support.
    /// 
    /// Install OpenAL Soft on macOS: brew install openal-soft
    /// </summary>
    private static void ConfigureOpenALForMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        // Homebrew paths for OpenAL Soft
        string[] possiblePaths =
        [
            "/opt/homebrew/opt/openal-soft/lib/libopenal.dylib", // Apple Silicon (M1/M2/M3)
            "/usr/local/opt/openal-soft/lib/libopenal.dylib",    // Intel Mac
            "/opt/homebrew/lib/libopenal.dylib",                  // Alternative Apple Silicon
            "/usr/local/lib/libopenal.dylib"                      // Alternative Intel
        ];

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                OpenALLibraryNameContainer.OverridePath = path;
                Console.WriteLine($"[OpenAL] Using OpenAL Soft from: {path}");
                return;
            }
        }

        // If OpenAL Soft is not found, fall back to Apple's OpenAL (may have capture issues)
        Console.WriteLine("[OpenAL] Warning: OpenAL Soft not found. Using Apple's deprecated OpenAL framework.");
        Console.WriteLine("[OpenAL] Audio capture may not work correctly on macOS.");
        Console.WriteLine("[OpenAL] For better audio capture support, install OpenAL Soft:");
        Console.WriteLine("[OpenAL]   brew install openal-soft");
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
