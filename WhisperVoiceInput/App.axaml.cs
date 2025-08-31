using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Serilog;
using Serilog.Core;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;
using WhisperVoiceInput.Services.LogViewer;
using WhisperVoiceInput.ViewModels;
// ReSharper disable RedundantCast

namespace WhisperVoiceInput;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private Logger? _logger;
    private MainWindowViewModel? _mainWindowViewModel;
    private SettingsService? _settingsService;
    private ActorSystemManager? _actorSystemManager;
    private ClipboardService? _clipboardService;
    private IGlobalHotkeyService? _globalHotkeyService;
    private ILogBufferService? _inMemoryLogBuffer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperVoiceInput",
            "logs");
        
        Directory.CreateDirectory(logPath);

        // Prepare in-memory log buffer sink
        var displayFormatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter(
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        var inMemoryLogBuffer = new InMemoryLogBufferService(displayFormatter, capacity: 100);
        var inMemorySink = new InMemorySerilogSink(inMemoryLogBuffer);
        _inMemoryLogBuffer = inMemoryLogBuffer;

        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(inMemorySink)
            .WriteTo.File(Path.Combine(logPath, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Seq("http://localhost:5341", queueSizeLimit: 10000)
            .CreateLogger();

        _logger.Information("Application starting");

        // Initialize SettingsService and MainWindowViewModel early
        _settingsService = new SettingsService(_logger);
        // Update in-memory buffer capacity from settings
        _inMemoryLogBuffer.UpdateCapacity(_settingsService.CurrentSettings.LogBufferCapacity);
        _mainWindowViewModel = new MainWindowViewModel(_logger, _settingsService);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Avoid duplicate validations
            DisableAvaloniaDataAnnotationValidation();

            if (_logger == null)
            {
                throw new InvalidOperationException("Logger not initialized");
            }

            if (_mainWindowViewModel == null)
            {
                throw new InvalidOperationException("MainWindowViewModel not initialized");
            }

            if (_settingsService == null)
            {
                throw new InvalidOperationException("SettingsService not initialized");
            }
            
            _globalHotkeyService = new GlobalHotkeyService(_logger);

            // Initialize clipboard service
            _clipboardService = new ClipboardService(_logger);

            // Initialize actor system
            _actorSystemManager = new ActorSystemManager(_logger);
            var propsFactory = new ActorPropsFactory(_logger);
            var retrySettings = new RetryPolicySettings
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30)
            };

            _actorSystemManager.Initialize(
                _settingsService,
                retrySettings,
                propsFactory,
                _clipboardService);
            

            // Initialize application view model with actor system
            var applicationViewModel = new ApplicationViewModel(
                desktop, 
                _logger, 
                _mainWindowViewModel, 
                _actorSystemManager as IRecordingToggler,
                _actorSystemManager as IStateObservableFactory,
                _clipboardService as IClipboardService,
                _globalHotkeyService as IGlobalHotkeyService,
                (_inMemoryLogBuffer as ILogBufferService)!);
            DataContext = applicationViewModel;
            
            desktop.Exit += (_, _) =>
            {
                _logger?.Information("Application shutting down");
                _globalHotkeyService?.Dispose();
                _actorSystemManager?.Dispose();
                _settingsService?.Dispose();
                _logger?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}