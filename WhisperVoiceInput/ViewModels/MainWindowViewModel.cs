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
    private string _serverAddress = string.Empty;
    private string _apiKey = string.Empty;
    private string _model = "whisper-large";
    private string _language = "en";
    private string _prompt = string.Empty;
    private bool _saveAudioFile;
    private string _audioFilePath = string.Empty;
    private ResultOutputType _outputType = ResultOutputType.Clipboard;
    
    private readonly ReactiveCommand<Unit, Unit> _saveSettingsCommand;

    public MainWindowViewModel(ILogger logger)
    {
        _logger = logger;
        
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
        ServerAddress = "http://localhost:5000";
        ApiKey = string.Empty;
        Model = "whisper-large";
        Language = "en";
        Prompt = string.Empty;
        SaveAudioFile = false;
        AudioFilePath = string.Empty;
        OutputType = ResultOutputType.Clipboard;
    }

    public string ServerAddress
    {
        get => _serverAddress;
        set => this.RaiseAndSetIfChanged(ref _serverAddress, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }

    public string Model
    {
        get => _model;
        set => this.RaiseAndSetIfChanged(ref _model, value);
    }

    public string Language
    {
        get => _language;
        set => this.RaiseAndSetIfChanged(ref _language, value);
    }

    public string Prompt
    {
        get => _prompt;
        set => this.RaiseAndSetIfChanged(ref _prompt, value);
    }

    public bool SaveAudioFile
    {
        get => _saveAudioFile;
        set => this.RaiseAndSetIfChanged(ref _saveAudioFile, value);
    }

    public string AudioFilePath
    {
        get => _audioFilePath;
        set => this.RaiseAndSetIfChanged(ref _audioFilePath, value);
    }

    public ResultOutputType OutputType
    {
        get => _outputType;
        set => this.RaiseAndSetIfChanged(ref _outputType, value);
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AppSettings
            {
                ServerAddress = ServerAddress,
                ApiKey = ApiKey,
                Model = Model,
                Language = Language,
                Prompt = Prompt,
                SaveAudioFile = SaveAudioFile,
                AudioFilePath = AudioFilePath,
                OutputType = OutputType
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
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
                    ServerAddress = settings.ServerAddress;
                    ApiKey = settings.ApiKey;
                    Model = settings.Model;
                    Language = settings.Language;
                    Prompt = settings.Prompt;
                    SaveAudioFile = settings.SaveAudioFile;
                    AudioFilePath = settings.AudioFilePath;
                    OutputType = settings.OutputType;
                    
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

    public AppSettings GetCurrentSettings()
    {
        return new AppSettings
        {
            ServerAddress = ServerAddress,
            ApiKey = ApiKey,
            Model = Model,
            Language = Language,
            Prompt = Prompt,
            SaveAudioFile = SaveAudioFile,
            AudioFilePath = AudioFilePath,
            OutputType = OutputType
        };
    }
}