using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.ViewModels;
using WhisperVoiceInput.Views;

namespace WhisperVoiceInput;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private ApplicationViewModel? _applicationViewModel;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private ILogger? _logger;
    private AppSettings? _settings;

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

        // Load settings
        _settings = LoadSettings();
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

            if (_settings == null)
            {
                throw new InvalidOperationException("Settings not initialized");
            }

            // Initialize application view model
            _applicationViewModel = new ApplicationViewModel(desktop, _logger, _settings);
            DataContext = _applicationViewModel;

            // Subscribe to window visibility changes
            _applicationViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ApplicationViewModel.MainWindowIsVisible))
                {
                    UpdateWindowVisibility();
                }
            };

            // Create the main window but don't show it
            _mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(_logger)
            };

            desktop.Exit += (_, _) =>
            {
                _applicationViewModel?.Dispose();
                _logger?.Information("Application shutting down");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private AppSettings LoadSettings()
    {
        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WhisperVoiceInput");
            
            Directory.CreateDirectory(appDataPath);
            var settingsPath = Path.Combine(appDataPath, "settings.json");

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) 
                    ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to load settings");
        }

        return new AppSettings();
    }

    private void UpdateWindowVisibility()
    {
        if (_applicationViewModel?.MainWindowIsVisible == true)
        {
            if (_mainWindow == null)
            {
                if (_logger == null)
                {
                    throw new InvalidOperationException("Logger not initialized");
                }

                _mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(_logger)
                };
            }

            _mainWindow.Show();
            _mainWindow.Activate();
        }
        else
        {
            _mainWindow?.Hide();
        }
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