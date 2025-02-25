using System;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private readonly AppState _appState;
    private readonly ReactiveCommand<Unit, Unit> _saveSettingsCommand;

    public MainWindowViewModel(ILogger logger, AppState appState)
    {
        _logger = logger;
        _appState = appState;
        
        _saveSettingsCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);

        // Initialize settings
        LoadSettings();

        // Setup property change subscription for auto-save
        this.WhenAnyValue(
                x => x.ServerAddress,
                x => x.ApiKey,
                x => x.Model,
                x => x.Language,
                x => x.Prompt,
                x => x.SaveAudioFile,
                x => x.AudioFilePath,
                x => x.OutputType,
                (_, _, _, _, _, _, _, _) => Unit.Default)
            .SubscribeOn(TaskPoolScheduler.Default)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .DistinctUntilChanged()
            .ObserveOn(TaskPoolScheduler.Default)
            .InvokeCommand(_saveSettingsCommand);
    }

    private string GetSettingsPath()
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var configDir = Path.Combine(appDataPath, "WhisperVoiceInput");
            
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
                _logger.Information("Created configuration directory: {Path}", configDir);
            }

            return Path.Combine(configDir, "settings.json");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create configuration directory, using local path");
            return "settings.json";
        }
    }

    private void InitializeDefaultValues()
    {
        _appState.Settings = new AppSettings
        {
            ServerAddress = "http://localhost:5000",
            ApiKey = string.Empty,
            Model = "whisper-large",
            Language = "en",
            Prompt = string.Empty,
            SaveAudioFile = false,
            AudioFilePath = string.Empty,
            OutputType = ResultOutputType.Clipboard
        };
    }

    public string ServerAddress
    {
        get => _appState.Settings.ServerAddress;
        set
        {
            if (_appState.Settings.ServerAddress != value)
            {
                _appState.Settings = _appState.Settings with { ServerAddress = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public string ApiKey
    {
        get => _appState.Settings.ApiKey;
        set
        {
            if (_appState.Settings.ApiKey != value)
            {
                _appState.Settings = _appState.Settings with { ApiKey = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public string Model
    {
        get => _appState.Settings.Model;
        set
        {
            if (_appState.Settings.Model != value)
            {
                _appState.Settings = _appState.Settings with { Model = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public string Language
    {
        get => _appState.Settings.Language;
        set
        {
            if (_appState.Settings.Language != value)
            {
                _appState.Settings = _appState.Settings with { Language = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public string Prompt
    {
        get => _appState.Settings.Prompt;
        set
        {
            if (_appState.Settings.Prompt != value)
            {
                _appState.Settings = _appState.Settings with { Prompt = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public bool SaveAudioFile
    {
        get => _appState.Settings.SaveAudioFile;
        set
        {
            if (_appState.Settings.SaveAudioFile != value)
            {
                _appState.Settings = _appState.Settings with { SaveAudioFile = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public string AudioFilePath
    {
        get => _appState.Settings.AudioFilePath;
        set
        {
            if (_appState.Settings.AudioFilePath != value)
            {
                _appState.Settings = _appState.Settings with { AudioFilePath = value };
                this.RaisePropertyChanged();
            }
        }
    }

    public ResultOutputType OutputType
    {
        get => _appState.Settings.OutputType;
        set
        {
            if (_appState.Settings.OutputType != value)
            {
                _appState.Settings = _appState.Settings with { OutputType = value };
                this.RaisePropertyChanged();
            }
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_appState.Settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var path = GetSettingsPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temporary file first, then move it
            var tempPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, path, true);

            _logger.Information("Settings saved successfully to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
        }
    }

    private void LoadSettings()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);

                if (settings != null)
                {
                    _appState.Settings = settings;
                    _logger.Information("Settings loaded successfully from {Path}", path);
                    return;
                }
            }

            _logger.Information("Settings file not found, using default values");
            InitializeDefaultValues();
            _saveSettingsCommand.Execute()
                .SubscribeOn(TaskPoolScheduler.Default)
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings, using default values");
            InitializeDefaultValues();
        }
    }
}