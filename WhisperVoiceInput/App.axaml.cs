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
using WhisperVoiceInput.Services;
using WhisperVoiceInput.ViewModels;

namespace WhisperVoiceInput;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private Logger? _logger;
    private MainWindowViewModel? _mainWindowViewModel;
    private SettingsService? _settingsService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperVoiceInput",
            "logs");
        
        Directory.CreateDirectory(logPath);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logPath, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Seq("http://localhost:5341", queueSizeLimit: 10000)
            .CreateLogger();

        _logger.Information("Application starting");

        // Initialize SettingsService and MainWindowViewModel early
        _settingsService = new SettingsService(_logger);
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

            // Initialize application view model
            var applicationViewModel = new ApplicationViewModel(desktop, _logger, _mainWindowViewModel, _settingsService);
            DataContext = applicationViewModel;
            
            desktop.Exit += (_, _) =>
            {
                _logger?.Information("Application shutting down");
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