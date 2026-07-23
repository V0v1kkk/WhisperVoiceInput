using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Logging;
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
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWaylandWithFallback()
            .With(CreateWaylandPlatformOptions())
            .With(new X11PlatformOptions
            {
                WmClass = "WhisperVoiceInput",
                EnableSessionManagement = false,
                GlProfiles = CreatePreferredGlProfiles(),
            })
            .WithInterFont()
            .UseReactiveUI(_ => { })
            .LogToTrace();

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

/// <summary>
/// Polyfill for UseWaylandWithFallback() added in Avalonia post-12.1.0 (PR #21734).
/// Tries native Wayland; on failure falls back to the previously registered backend (X11).
/// Safe no-op on Windows and macOS. Remove when upgrading to Avalonia >= 12.1.1.
/// </summary>
static class WaylandFallbackExtensions
{
    public static AppBuilder UseWaylandWithFallback(this AppBuilder builder)
    {
        if (!OperatingSystem.IsLinux())
            return builder;

        var fallback = builder.WindowingSubsystemInitializer
            ?? throw new InvalidOperationException(
                "A fallback windowing backend must be configured before calling " +
                "UseWaylandWithFallback (e.g. via UsePlatformDetect or UseX11).");

        builder.UseWayland();
        var waylandInit = builder.WindowingSubsystemInitializer!;

        builder.UseWindowingSubsystem(() =>
        {
            try
            {
                waylandInit();
            }
            catch (Exception ex)
            {
                Logger.TryGet(LogEventLevel.Warning, LogArea.Platform)?.Log(
                    null,
                    "Unable to initialize Wayland backend, falling back to X11: {Error}",
                    ex.Message);
                fallback();
            }
        });

        return builder;
    }
}
