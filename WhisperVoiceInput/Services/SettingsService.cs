using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Services
{
    public class SettingsService : ReactiveObject, IDisposable
    {
        private readonly ILogger _logger;
        private readonly BehaviorSubject<AppSettings> _settings;
        private AppSettings _lastSavedSettings;
        private bool _isInitialLoad = true;
        
        // Observable properties for each setting
        private readonly ObservableAsPropertyHelper<string> _serverAddress;
        private readonly ObservableAsPropertyHelper<string> _apiKey;
        private readonly ObservableAsPropertyHelper<string> _model;
        private readonly ObservableAsPropertyHelper<string> _language;
        private readonly ObservableAsPropertyHelper<string> _prompt;
        private readonly ObservableAsPropertyHelper<bool> _saveAudioFile;
        private readonly ObservableAsPropertyHelper<string> _audioFilePath;
        private readonly ObservableAsPropertyHelper<ResultOutputType> _outputType;
        
        // Current settings as an observable
        public IObservable<AppSettings> Settings => _settings.AsObservable();
        
        // Individual settings properties
        public string ServerAddress => _serverAddress.Value;
        public string ApiKey => _apiKey.Value;
        public string Model => _model.Value;
        public string Language => _language.Value;
        public string Prompt => _prompt.Value;
        public bool SaveAudioFile => _saveAudioFile.Value;
        public string AudioFilePath => _audioFilePath.Value;
        public ResultOutputType OutputType => _outputType.Value;
        
        // Current settings snapshot
        public AppSettings CurrentSettings => _settings.Value;

        public SettingsService(ILogger logger)
        {
            _logger = logger.ForContext<SettingsService>();
            
            // Initialize with default settings
            var defaultSettings = new AppSettings
            {
                ServerAddress = "http://localhost:5000",
                ApiKey = string.Empty,
                Model = "whisper-large",
                Language = "en",
                Prompt = string.Empty,
                SaveAudioFile = false,
                AudioFilePath = string.Empty,
                OutputType = ResultOutputType.ClipboardAvaloniaApi
            };
            
            _settings = new BehaviorSubject<AppSettings>(defaultSettings);
            _lastSavedSettings = defaultSettings;
            
            // Set up observable properties
            _serverAddress = _settings
                .Select(s => s.ServerAddress)
                .ToProperty(this, nameof(ServerAddress));
                
            _apiKey = _settings
                .Select(s => s.ApiKey)
                .ToProperty(this, nameof(ApiKey));
                
            _model = _settings
                .Select(s => s.Model)
                .ToProperty(this, nameof(Model));
                
            _language = _settings
                .Select(s => s.Language)
                .ToProperty(this, nameof(Language));
                
            _prompt = _settings
                .Select(s => s.Prompt)
                .ToProperty(this, nameof(Prompt));
                
            _saveAudioFile = _settings
                .Select(s => s.SaveAudioFile)
                .ToProperty(this, nameof(SaveAudioFile));
                
            _audioFilePath = _settings
                .Select(s => s.AudioFilePath)
                .ToProperty(this, nameof(AudioFilePath));
                
            _outputType = _settings
                .Select(s => s.OutputType)
                .ToProperty(this, nameof(OutputType));
                
            // Load settings on initialization
            LoadSettings();
        }
        
        // Update a specific setting
        public void UpdateSetting(Func<AppSettings, AppSettings> updateFunction)
        {
            var currentSettings = _settings.Value;
            var newSettings = updateFunction(currentSettings);
            
            // Only update if settings actually changed
            if (!AreSettingsEqual(currentSettings, newSettings))
            {
                _settings.OnNext(newSettings);
                
                // Save settings when they change (but not during initial load)
                if (!_isInitialLoad)
                {
                    _ = SaveSettingsAsync();
                }
            }
        }
        
        // Check if two settings objects are equal
        private bool AreSettingsEqual(AppSettings a, AppSettings b)
        {
            return a.ServerAddress == b.ServerAddress &&
                   a.ApiKey == b.ApiKey &&
                   a.Model == b.Model &&
                   a.Language == b.Language &&
                   a.Prompt == b.Prompt &&
                   a.SaveAudioFile == b.SaveAudioFile &&
                   a.AudioFilePath == b.AudioFilePath &&
                   a.OutputType == b.OutputType;
        }
        
        // Update server address
        public void SetServerAddress(string value)
        {
            UpdateSetting(s => s with { ServerAddress = value });
        }
        
        // Update API key
        public void SetApiKey(string value)
        {
            UpdateSetting(s => s with { ApiKey = value });
        }
        
        // Update model
        public void SetModel(string value)
        {
            UpdateSetting(s => s with { Model = value });
        }
        
        // Update language
        public void SetLanguage(string value)
        {
            UpdateSetting(s => s with { Language = value });
        }
        
        // Update prompt
        public void SetPrompt(string value)
        {
            UpdateSetting(s => s with { Prompt = value });
        }
        
        // Update save audio file flag
        public void SetSaveAudioFile(bool value)
        {
            UpdateSetting(s => s with { SaveAudioFile = value });
        }
        
        // Update audio file path
        public void SetAudioFilePath(string value)
        {
            UpdateSetting(s => s with { AudioFilePath = value });
        }
        
        // Update output type
        public void SetOutputType(ResultOutputType value)
        {
            UpdateSetting(s => s with { OutputType = value });
        }
        
        // Get the path to the settings file
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
        
        // Load settings from disk
        public void LoadSettings()
        {
            _isInitialLoad = true;
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    
                    if (settings != null)
                    {
                        _settings.OnNext(settings);
                        _lastSavedSettings = settings;
                        _logger.Information("Settings loaded successfully from {Path}", path);
                        _isInitialLoad = false;
                        return;
                    }
                }
                
                _logger.Information("Settings file not found, using default values");
                // Default values are already set in the constructor
                _ = SaveSettingsAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load settings, using default values");
                // Default values are already set in the constructor
            }
            finally
            {
                _isInitialLoad = false;
            }
        }
        
        // Save settings to disk
        public async Task SaveSettingsAsync()
        {
            // Don't save if settings haven't changed
            if (AreSettingsEqual(_settings.Value, _lastSavedSettings))
            {
                _logger.Debug("Settings unchanged, skipping save");
                return;
            }
            
            try
            {
                var json = JsonSerializer.Serialize(_settings.Value, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                var path = GetSettingsPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Try to write directly to the file first
                try
                {
                    await File.WriteAllTextAsync(path, json);
                    _lastSavedSettings = _settings.Value;
                    _logger.Information("Settings saved successfully to {Path}", path);
                    return;
                }
                catch (IOException ex)
                {
                    _logger.Warning(ex, "Could not write directly to settings file, trying alternative approach");
                }
                
                // If direct write fails, try with a temporary file
                try
                {
                    var tempPath = Path.GetTempFileName();
                    await File.WriteAllTextAsync(tempPath, json);
                    
                    // Try to copy the file instead of moving it
                    File.Copy(tempPath, path, true);
                    File.Delete(tempPath);
                    
                    _lastSavedSettings = _settings.Value;
                    _logger.Information("Settings saved successfully to {Path} using temp file", path);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save settings using temp file");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings");
            }
        }
        
        public void Dispose()
        {
            _settings.Dispose();
            _serverAddress.Dispose();
            _apiKey.Dispose();
            _model.Dispose();
            _language.Dispose();
            _prompt.Dispose();
            _saveAudioFile.Dispose();
            _audioFilePath.Dispose();
            _outputType.Dispose();
        }
    }
}