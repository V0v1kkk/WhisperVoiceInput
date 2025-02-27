﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private readonly SettingsService _settingsService;
    private readonly ReactiveCommand<Unit, Unit> _saveSettingsCommand;
    
    // Observable properties for settings
    private readonly ObservableAsPropertyHelper<string> _serverAddress;
    private readonly ObservableAsPropertyHelper<string> _apiKey;
    private readonly ObservableAsPropertyHelper<string> _model;
    private readonly ObservableAsPropertyHelper<string> _language;
    private readonly ObservableAsPropertyHelper<string> _prompt;
    private readonly ObservableAsPropertyHelper<bool> _saveAudioFile;
    private readonly ObservableAsPropertyHelper<string> _audioFilePath;
    private readonly ObservableAsPropertyHelper<ResultOutputType> _outputType;
    
    // Path validation properties
    private readonly ObservableAsPropertyHelper<bool> _isAudioFilePathValid;
    private readonly ObservableAsPropertyHelper<string> _audioFilePathValidationMessage;
    
    // Mutable backing fields for two-way binding
    private string _serverAddressInput = string.Empty;
    private string _apiKeyInput = string.Empty;
    private string _modelInput = string.Empty;
    private string _languageInput = string.Empty;
    private string _promptInput = string.Empty;
    private bool _saveAudioFileInput;
    private string _audioFilePathInput = string.Empty;
    private ResultOutputType _outputTypeInput;

#pragma warning disable CS8618, CS9264
    public MainWindowViewModel() {} // For design-time data context
#pragma warning restore CS8618, CS9264
    
    public MainWindowViewModel(ILogger logger, SettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
        
        _saveSettingsCommand = ReactiveCommand.Create(SaveSettings);
        
        // Set up observable properties from settings service
        _serverAddress = _settingsService.Settings
            .Select(s => s.ServerAddress)
            .ToProperty(this, nameof(ServerAddress));
            
        _apiKey = _settingsService.Settings
            .Select(s => s.ApiKey)
            .ToProperty(this, nameof(ApiKey));
            
        _model = _settingsService.Settings
            .Select(s => s.Model)
            .ToProperty(this, nameof(Model));
            
        _language = _settingsService.Settings
            .Select(s => s.Language)
            .ToProperty(this, nameof(Language));
            
        _prompt = _settingsService.Settings
            .Select(s => s.Prompt)
            .ToProperty(this, nameof(Prompt));
            
        _saveAudioFile = _settingsService.Settings
            .Select(s => s.SaveAudioFile)
            .ToProperty(this, nameof(SaveAudioFile));
            
        _audioFilePath = _settingsService.Settings
            .Select(s => s.AudioFilePath)
            .ToProperty(this, nameof(AudioFilePath));
            
        _outputType = _settingsService.Settings
            .Select(s => s.OutputType)
            .ToProperty(this, nameof(OutputType));
            
        // Initialize input properties from settings
        _serverAddressInput = _settingsService.ServerAddress;
        _apiKeyInput = _settingsService.ApiKey;
        _modelInput = _settingsService.Model;
        _languageInput = _settingsService.Language;
        _promptInput = _settingsService.Prompt;
        _saveAudioFileInput = _settingsService.SaveAudioFile;
        _audioFilePathInput = _settingsService.AudioFilePath;
        _outputTypeInput = _settingsService.OutputType;
        
        // Set up path validation
        var audioFilePathValidation = this.WhenAnyValue(
                x => x.AudioFilePathInput,
                x => x.SaveAudioFileInput,
                (path, saveEnabled) => new { Path = path, SaveEnabled = saveEnabled })
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler);
            
        _isAudioFilePathValid = audioFilePathValidation
            .Select(x => x.SaveEnabled ? ValidateAudioFilePath(x.Path) : ValidationResult.Success)
            .Select(result => result.IsValid)
            .ToProperty(this, nameof(IsAudioFilePathValid));
            
        _audioFilePathValidationMessage = audioFilePathValidation
            .Select(x => x.SaveEnabled ? ValidateAudioFilePath(x.Path) : ValidationResult.Success)
            .Select(result => result.Message)
            .ToProperty(this, nameof(AudioFilePathValidationMessage));
            
        // Set up folder selection command
        SelectFolderCommand = ReactiveCommand.CreateFromTask(
            SelectFolderAsync,
            this.WhenAnyValue(x => x.SaveAudioFileInput),
            AvaloniaScheduler.Instance);
        
        // Subscribe to settings changes to update input properties
        this.WhenAnyValue(x => x.ServerAddress)
            .Subscribe(value => ServerAddressInput = value);
            
        this.WhenAnyValue(x => x.ApiKey)
            .Subscribe(value => ApiKeyInput = value);
            
        this.WhenAnyValue(x => x.Model)
            .Subscribe(value => ModelInput = value);
            
        this.WhenAnyValue(x => x.Language)
            .Subscribe(value => LanguageInput = value);
            
        this.WhenAnyValue(x => x.Prompt)
            .Subscribe(value => PromptInput = value);
            
        this.WhenAnyValue(x => x.SaveAudioFile)
            .Subscribe(value => SaveAudioFileInput = value);
            
        this.WhenAnyValue(x => x.AudioFilePath)
            .Subscribe(value => AudioFilePathInput = value);
            
        this.WhenAnyValue(x => x.OutputType)
            .Subscribe(value => OutputTypeInput = value);
            
        // Setup property change subscription for auto-save
        this.WhenAnyValue(
                x => x.ServerAddressInput,
                x => x.ApiKeyInput,
                x => x.ModelInput,
                x => x.LanguageInput,
                x => x.PromptInput,
                x => x.SaveAudioFileInput,
                x => x.AudioFilePathInput,
                x => x.OutputTypeInput,
                (_, _, _, _, _, _, _, _) => Unit.Default)
            .SubscribeOn(TaskPoolScheduler.Default)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(TaskPoolScheduler.Default)
            .InvokeCommand(_saveSettingsCommand);
    }

    private void SaveSettings()
    {
        _settingsService.SetServerAddress(ServerAddressInput);
        _settingsService.SetApiKey(ApiKeyInput);
        _settingsService.SetModel(ModelInput);
        _settingsService.SetLanguage(LanguageInput);
        _settingsService.SetPrompt(PromptInput);
        
        switch (SaveAudioFileInput)
        {
            case false:
                _settingsService.SetSaveAudioFile(SaveAudioFileInput);
                break;
            case true when IsAudioFilePathValid:
                _settingsService.SetSaveAudioFile(SaveAudioFileInput);
                _settingsService.SetAudioFilePath(AudioFilePathInput);
                break;
        }
        
        _settingsService.SetOutputType(OutputTypeInput);
    }

    // Read-only properties that get values from the settings service
    public string ServerAddress => _serverAddress.Value;
    public string ApiKey => _apiKey.Value;
    public string Model => _model.Value;
    public string Language => _language.Value;
    public string Prompt => _prompt.Value;
    public bool SaveAudioFile => _saveAudioFile.Value;
    public string AudioFilePath => _audioFilePath.Value;
    public ResultOutputType OutputType => _outputType.Value;
    
    // Two-way bindable properties
    public string ServerAddressInput
    {
        get => _serverAddressInput;
        set => this.RaiseAndSetIfChanged(ref _serverAddressInput, value);
    }
    
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set => this.RaiseAndSetIfChanged(ref _apiKeyInput, value);
    }
    
    public string ModelInput
    {
        get => _modelInput;
        set => this.RaiseAndSetIfChanged(ref _modelInput, value);
    }
    
    public string LanguageInput
    {
        get => _languageInput;
        set => this.RaiseAndSetIfChanged(ref _languageInput, value);
    }
    
    public string PromptInput
    {
        get => _promptInput;
        set => this.RaiseAndSetIfChanged(ref _promptInput, value);
    }
    
    public bool SaveAudioFileInput
    {
        get => _saveAudioFileInput;
        set => this.RaiseAndSetIfChanged(ref _saveAudioFileInput, value);
    }
    
    public string AudioFilePathInput
    {
        get => _audioFilePathInput;
        set => this.RaiseAndSetIfChanged(ref _audioFilePathInput, value);
    }
    
    public ResultOutputType OutputTypeInput
    {
        get => _outputTypeInput;
        set => this.RaiseAndSetIfChanged(ref _outputTypeInput, value);
    }
    
    // Path validation properties
    public bool IsAudioFilePathValid => _isAudioFilePathValid.Value;
    public string AudioFilePathValidationMessage => _audioFilePathValidationMessage.Value;
    public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; }
    
    // No notification properties or methods needed
    
    // Validation result class
    private class ValidationResult(bool isValid, string message = "")
    {
        public bool IsValid { get; } = isValid;
        public string Message { get; } = message;

        public static ValidationResult Success => new(true);
        public static ValidationResult Error(string message) => new(false, message);
    }
    
    // Validate the audio file path
    private ValidationResult ValidateAudioFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ValidationResult.Error("Path cannot be empty");
        }
        
        try
        {
            // Check if path exists
            if (!Directory.Exists(path))
            {
                return ValidationResult.Error("Directory does not exist");
            }
            
            // Check write permissions
            try
            {
                var testFilePath = Path.Combine(path, $"test_{Guid.NewGuid()}.tmp");
                // File is automatically closed and deleted when disposed
                using var _ = File.Create(testFilePath, 1, FileOptions.DeleteOnClose);
            }
            catch (UnauthorizedAccessException)
            {
                return ValidationResult.Error("You don't have permission to write to this directory");
            }
            
            // Check path length
            if (path.Length > 260) // Windows path length limit
            {
                return ValidationResult.Error("Path is too long");
            }
            
            // Check for invalid characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.IndexOfAny(invalidChars) >= 0)
            {
                return ValidationResult.Error("Path contains invalid characters");
            }
            
            return ValidationResult.Success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error validating path: {Path}", path);
            return ValidationResult.Error($"Error validating path: {ex.Message}");
        }
    }
    
    // Select folder using dialog
    private async Task SelectFolderAsync()
    {
        try
        {
            // Find the main window
            var mainWindow = Application.Current?.ApplicationLifetime is 
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
                
            if (mainWindow == null)
            {
                _logger.Error("Could not get main window for folder dialog");
                return;
            }
            
            // Use the StorageProvider API
            var options = new FolderPickerOpenOptions
            {
                Title = "Select Folder for Audio Files",
                AllowMultiple = false
            };
            
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(options);
            if (folders.Count > 0)
            {
                var folder = folders[0];
                var folderPath = folder.TryGetLocalPath();
                
                if (!string.IsNullOrEmpty(folderPath))
                {
                    AudioFilePathInput = folderPath;
                }
                else
                {
                    _logger.Warning("Selected folder does not have a local path");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error selecting folder");
        }
    }
    
    // Clean up resources
    public override void Dispose()
    {
        base.Dispose();
        _serverAddress.Dispose();
        _apiKey.Dispose();
        _model.Dispose();
        _language.Dispose();
        _prompt.Dispose();
        _saveAudioFile.Dispose();
        _audioFilePath.Dispose();
        _outputType.Dispose();
        _isAudioFilePathValid.Dispose();
        _audioFilePathValidationMessage.Dispose();
        _saveSettingsCommand.Dispose();
        SelectFolderCommand.Dispose();
    }
}