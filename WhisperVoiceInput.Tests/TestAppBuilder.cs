using Avalonia;
using Avalonia.Headless;
using ReactiveUI.Avalonia;
using WhisperVoiceInput;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseReactiveUI(_ => { });
}
