using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using ReactiveUI.Avalonia;

namespace WhisperVoiceInput;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI(_ => { })
            .LogToTrace();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ConfigureLinux(builder);

        return builder;
    }

    private static void ConfigureLinux(AppBuilder builder)
    {
        builder
            .UseWayland()
            .With(CreateWaylandPlatformOptions())
            .With(new X11PlatformOptions
            {
                WmClass = "WhisperVoiceInput",
                EnableSessionManagement = false,
                GlProfiles = CreatePreferredGlProfiles(),
            });
    }

    private static WaylandPlatformOptions CreateWaylandPlatformOptions()
    {
        var options = new WaylandPlatformOptions
        {
            GlProfiles = CreatePreferredGlProfiles(),
            UseDmabufSwapchain = false,
        };

        // Escape hatch: skip EGL init entirely and use Wayland shm (software) rendering only.
        if (Environment.GetEnvironmentVariable("WHISPERVOICEINPUT_WAYLAND_SOFTWARE") == "1")
            options.GlProfiles = [];

        return options;
    }

    /// <summary>
    /// Prefer OpenGL ES on Wayland/EGL. Desktop GL profiles are known to fail with recent
    /// NVIDIA drivers on the native Wayland backend (Avalonia PR #21448).
    /// </summary>
    private static IList<GlVersion> CreatePreferredGlProfiles() =>
    [
        new(GlProfileType.OpenGLES, 3, 2),
        new(GlProfileType.OpenGLES, 3, 0),
        new(GlProfileType.OpenGLES, 2, 0),
    ];
}
