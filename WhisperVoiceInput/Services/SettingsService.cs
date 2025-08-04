using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Abstractions;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Services;

public class SettingsService : ReactiveObject, IDisposable, ISettingsService
{
    private readonly ILogger _logger;
    private readonly Lock _updateLock = new();
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly BehaviorSubject<AppSettings> _settingsSubject;
    private readonly IDisposable _saveSubscription;
    private AppSettings _settings;
    private AppSettings _lastSavedSettings;
        
    public SettingsService(ILogger logger)
    {
        _logger = logger.ForContext<SettingsService>();
            
        // Initialize with default settings
        _settings = new AppSettings
        {
            ServerAddress = "http://localhost:5000",
            ApiKey = string.Empty,
            Model = "whisper-large",
            Language = "en",
            Prompt = string.Empty,
            SaveAudioFile = false,
            AudioFilePath = string.Empty,
            OutputType = ResultOutputType.ClipboardAvaloniaApi,
            PostProcessingEnabled = false,
            PostProcessingApiUrl = "http://localhost:11434",
            PostProcessingModelName = "llama3.2",
            PostProcessingApiKey = string.Empty,
            PostProcessingPrompt = "Improve and format the following text:"
        };
            
        _lastSavedSettings = _settings;
        _settingsSubject = new BehaviorSubject<AppSettings>(_settings);
            
        // Load settings first
        LoadSettings();
            
        // Then set up throttled save subscription to avoid triggering on initial load
        _saveSubscription = _settingsSubject
            //.Skip(1) // Skip initial value
            .Throttle(TimeSpan.FromMilliseconds(500))
            .DistinctUntilChanged()
            .Subscribe(async void (settings) =>
            {
                try
                {
                    await SaveSettingsAsync(settings);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to save settings");
                }
            });
    }
        
    // Observable settings for actor system
    public IObservable<AppSettings> Settings => _settingsSubject.AsObservable();
        
    // Current settings snapshot (no lock needed - atomic reference read)
    public AppSettings CurrentSettings => _settings;
        
    // Helper method to update settings with proper CallerMemberName propagation
    private void UpdateSettings(Func<AppSettings, AppSettings> updateFunc, [CallerMemberName] string? propertyName = null)
    {
        lock (_updateLock)
        {
            var newSettings = updateFunc(_settings);
            if (newSettings == _settings) return; // No change
                
            _settings = newSettings;
            this.RaisePropertyChanged(propertyName);
            _settingsSubject.OnNext(_settings);
        }
    }
        
    // Individual properties with change notification and thread safety
    public string ServerAddress
    {
        get => _settings.ServerAddress; // No lock needed for atomic read
        set => UpdateSettings(s => s with { ServerAddress = value });
    }
        
    public string ApiKey
    {
        get => _settings.ApiKey;
        set => UpdateSettings(s => s with { ApiKey = value });
    }
        
    public string Model
    {
        get => _settings.Model;
        set => UpdateSettings(s => s with { Model = value });
    }
        
    public string Language
    {
        get => _settings.Language;
        set => UpdateSettings(s => s with { Language = value });
    }
        
    public string Prompt
    {
        get => _settings.Prompt;
        set => UpdateSettings(s => s with { Prompt = value });
    }
        
    public bool SaveAudioFile
    {
        get => _settings.SaveAudioFile;
        set => UpdateSettings(s => s with { SaveAudioFile = value });
    }
        
    public string AudioFilePath
    {
        get => _settings.AudioFilePath;
        set => UpdateSettings(s => s with { AudioFilePath = value });
    }
        
    public ResultOutputType OutputType
    {
        get => _settings.OutputType;
        set => UpdateSettings(s => s with { OutputType = value });
    }
        
    // Post-processing properties
    public bool PostProcessingEnabled
    {
        get => _settings.PostProcessingEnabled;
        set => UpdateSettings(s => s with { PostProcessingEnabled = value });
    }
        
    public string PostProcessingApiUrl
    {
        get => _settings.PostProcessingApiUrl;
        set => UpdateSettings(s => s with { PostProcessingApiUrl = value });
    }
        
    public string PostProcessingModelName
    {
        get => _settings.PostProcessingModelName;
        set => UpdateSettings(s => s with { PostProcessingModelName = value });
    }
        
    public string PostProcessingApiKey
    {
        get => _settings.PostProcessingApiKey;
        set => UpdateSettings(s => s with { PostProcessingApiKey = value });
    }
        
    public string PostProcessingPrompt
    {
        get => _settings.PostProcessingPrompt;
        set => UpdateSettings(s => s with { PostProcessingPrompt = value });
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
                    lock (_updateLock)
                    {
                        _settings = settings;
                        _lastSavedSettings = settings; // Mark as saved
                        _settingsSubject.OnNext(_settings);
                    }
                        
                    _logger.Information("Settings loaded successfully from {Path}", path);
                        
                    // Raise property changed for all properties to notify bindings
                    this.RaisePropertyChanged(nameof(ServerAddress));
                    this.RaisePropertyChanged(nameof(ApiKey));
                    this.RaisePropertyChanged(nameof(Model));
                    this.RaisePropertyChanged(nameof(Language));
                    this.RaisePropertyChanged(nameof(Prompt));
                    this.RaisePropertyChanged(nameof(SaveAudioFile));
                    this.RaisePropertyChanged(nameof(AudioFilePath));
                    this.RaisePropertyChanged(nameof(OutputType));
                    this.RaisePropertyChanged(nameof(PostProcessingEnabled));
                    this.RaisePropertyChanged(nameof(PostProcessingApiUrl));
                    this.RaisePropertyChanged(nameof(PostProcessingModelName));
                    this.RaisePropertyChanged(nameof(PostProcessingApiKey));
                    this.RaisePropertyChanged(nameof(PostProcessingPrompt));
                    return;
                }
            }
                
            _logger.Information("Settings file not found, using default values");
            _ = SaveSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings, using default values");
        }
    }
        
    // Save settings to disk with concurrency protection and duplicate prevention
    private async Task SaveSettingsAsync(AppSettings settingsToSave)
    {
        // Quick check to avoid unnecessary saves
        if (settingsToSave == _lastSavedSettings)
        {
            _logger.Debug("Settings unchanged, skipping save");
            return;
        }
            
        if (!await _saveSemaphore.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            _logger.Warning("Failed to acquire save semaphore within timeout");
            return;
        }
            
        try
        {
            // Double-check after acquiring semaphore
            if (settingsToSave == _lastSavedSettings)
            {
                _logger.Debug("Settings unchanged after acquiring semaphore, skipping save");
                return;
            }
                
            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions
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
                _lastSavedSettings = settingsToSave;
                _logger.Debug("Settings saved successfully to {Path}", path);
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
                    
                _lastSavedSettings = settingsToSave;
                _logger.Debug("Settings saved successfully to {Path} using temp file", path);
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
        finally
        {
            _saveSemaphore.Release();
        }
    }
        
    public void Dispose()
    {
        _saveSubscription.Dispose();
        _settingsSubject.Dispose();
        _saveSemaphore.Dispose();
    }
}