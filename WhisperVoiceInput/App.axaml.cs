using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.ViewModels;
using WhisperVoiceInput.Views;

namespace WhisperVoiceInput;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private ILogger? _logger;
    private MainWindowViewModel? _mainWindowViewModel;

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
            .WriteTo.Seq("http://localhost:5341")
            .CreateLogger();

        _logger.Information("Application starting");

        // Initialize MainWindowViewModel early
        _mainWindowViewModel = new MainWindowViewModel(_logger);
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

            // Initialize application view model
            var applicationViewModel = new ApplicationViewModel(desktop, _logger, _mainWindowViewModel);
            DataContext = applicationViewModel;
            
            desktop.Exit += (_, _) =>
            {
                _logger?.Information("Application shutting down");
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