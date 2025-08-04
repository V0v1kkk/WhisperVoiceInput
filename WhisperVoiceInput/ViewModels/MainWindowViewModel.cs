using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;
using ReactiveUI.Validation.States;
using Serilog;
using WhisperVoiceInput.Models;
using WhisperVoiceInput.Services;

namespace WhisperVoiceInput.ViewModels;

public partial class MainWindowViewModel : ReactiveValidationObject
{
    private readonly ILogger _logger;
    private readonly SettingsService _settingsService;
    
    // Two-way bindable input properties for UI using source generation
    [Reactive] public partial string ServerAddressInput { get; set; } = string.Empty;
    [Reactive] public partial string ApiKeyInput { get; set; } = string.Empty;
    [Reactive] public partial string ModelInput { get; set; } = string.Empty;
    [Reactive] public partial string LanguageInput { get; set; } = string.Empty;
    [Reactive] public partial string PromptInput { get; set; } = string.Empty;
    [Reactive] public partial bool SaveAudioFileInput { get; set; }
    [Reactive] public partial string AudioFilePathInput { get; set; } = string.Empty;
    [Reactive] public partial ResultOutputType OutputTypeInput { get; set; }
    [Reactive] public partial bool PostProcessingEnabledInput { get; set; }
    [Reactive] public partial string PostProcessingApiUrlInput { get; set; } = string.Empty;
    [Reactive] public partial string PostProcessingModelNameInput { get; set; } = string.Empty;
    [Reactive] public partial string PostProcessingApiKeyInput { get; set; } = string.Empty;
    [Reactive] public partial string PostProcessingPromptInput { get; set; } = string.Empty;
    
    public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; }

#pragma warning disable CS8618, CS9264
    public MainWindowViewModel() {} // For design-time data context
#pragma warning restore CS8618, CS9264
    
    public MainWindowViewModel(ILogger logger, SettingsService settingsService)
    {
        _logger = logger.ForContext<MainWindowViewModel>();
        _settingsService = settingsService;
        
        // Initialize input properties with current settings values
        ServerAddressInput = _settingsService.ServerAddress;
        ApiKeyInput = _settingsService.ApiKey;
        ModelInput = _settingsService.Model;
        LanguageInput = _settingsService.Language;
        PromptInput = _settingsService.Prompt;
        SaveAudioFileInput = _settingsService.SaveAudioFile;
        AudioFilePathInput = _settingsService.AudioFilePath;
        OutputTypeInput = _settingsService.OutputType;
        PostProcessingEnabledInput = _settingsService.PostProcessingEnabled;
        PostProcessingApiUrlInput = _settingsService.PostProcessingApiUrl;
        PostProcessingModelNameInput = _settingsService.PostProcessingModelName;
        PostProcessingApiKeyInput = _settingsService.PostProcessingApiKey;
        PostProcessingPromptInput = _settingsService.PostProcessingPrompt;
        
        SetupValidationRules();
        SetupSettingsSynchronization();
        
        // Set up folder selection command
        SelectFolderCommand = ReactiveCommand.CreateFromTask(
            SelectFolderAsync,
            this.WhenAnyValue(x => x.SaveAudioFileInput),
            AvaloniaScheduler.Instance);
    }

    private void SetupValidationRules()
    {
        // Basic required field validations
        this.ValidationRule(
            vm => vm.ServerAddressInput,
            this.WhenAnyValue(x => x.ServerAddressInput, addr => !string.IsNullOrWhiteSpace(addr)),
            "Server address is required");
            
        this.ValidationRule(
            vm => vm.ModelInput,
            this.WhenAnyValue(x => x.ModelInput, model => !string.IsNullOrWhiteSpace(model)),
            "Model is required");
        
        // Conditional validation for audio file path - only validate when save is enabled
        var audioPathValidation = this.WhenAnyValue(
                x => x.SaveAudioFileInput,
                x => x.AudioFilePathInput,
                (saveEnabled, path) => new { SaveEnabled = saveEnabled, Path = path })
            .Select(state => 
            {
                // Only validate if save audio file is enabled
                if (!state.SaveEnabled)
                    return ValidationState.Valid;

                // Validate directory path when save is enabled
                if (string.IsNullOrWhiteSpace(state.Path))
                    return new ValidationState(false, "Directory path is required when audio saving is enabled");

                if (!Directory.Exists(state.Path))
                    return new ValidationState(false, "Directory does not exist");

                // Check write permissions
                try
                {
                    var testFilePath = Path.Combine(state.Path, $"test_{Guid.NewGuid()}.tmp");
                    using var _ = File.Create(testFilePath, 1, FileOptions.DeleteOnClose);
                }
                catch (UnauthorizedAccessException)
                {
                    return new ValidationState(false, "You don't have permission to write to this directory");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error validating path: {Path}", state.Path);
                    return new ValidationState(false, $"Error validating path: {ex.Message}");
                }

                return ValidationState.Valid;
            });

        this.ValidationRule(vm => vm.AudioFilePathInput, audioPathValidation);
        
        // Conditional validation for post-processing API URL - only validate when post-processing is enabled
        var postProcessingApiUrlValidation = this.WhenAnyValue(
                x => x.PostProcessingEnabledInput,
                x => x.PostProcessingApiUrlInput,
                (enabled, apiUrl) => !enabled || !string.IsNullOrWhiteSpace(apiUrl));

        this.ValidationRule(
            vm => vm.PostProcessingApiUrlInput,
            postProcessingApiUrlValidation,
            "API URL is required when post-processing is enabled");
        
        // Conditional validation for post-processing model name
        var postProcessingModelValidation = this.WhenAnyValue(
                x => x.PostProcessingEnabledInput,
                x => x.PostProcessingModelNameInput,
                (enabled, modelName) => !enabled || !string.IsNullOrWhiteSpace(modelName));

        this.ValidationRule(
            vm => vm.PostProcessingModelNameInput,
            postProcessingModelValidation,
            "Model name is required when post-processing is enabled");
    }

    private void SetupSettingsSynchronization()
    {
        // Subscribe to settings service changes to update input properties
        _settingsService.WhenAnyValue(x => x.ServerAddress)
            .Where(value => value != ServerAddressInput)
            .Subscribe(value => ServerAddressInput = value);
            
        _settingsService.WhenAnyValue(x => x.ApiKey)
            .Where(value => value != ApiKeyInput)
            .Subscribe(value => ApiKeyInput = value);
            
        _settingsService.WhenAnyValue(x => x.Model)
            .Where(value => value != ModelInput)
            .Subscribe(value => ModelInput = value);
            
        _settingsService.WhenAnyValue(x => x.Language)
            .Where(value => value != LanguageInput)
            .Subscribe(value => LanguageInput = value);
            
        _settingsService.WhenAnyValue(x => x.Prompt)
            .Where(value => value != PromptInput)
            .Subscribe(value => PromptInput = value);
            
        _settingsService.WhenAnyValue(x => x.SaveAudioFile)
            .Where(value => value != SaveAudioFileInput)
            .Subscribe(value => SaveAudioFileInput = value);
            
        _settingsService.WhenAnyValue(x => x.AudioFilePath)
            .Where(value => value != AudioFilePathInput)
            .Subscribe(value => AudioFilePathInput = value);
            
        _settingsService.WhenAnyValue(x => x.OutputType)
            .Where(value => value != OutputTypeInput)
            .Subscribe(value => OutputTypeInput = value);
            
        _settingsService.WhenAnyValue(x => x.PostProcessingEnabled)
            .Where(value => value != PostProcessingEnabledInput)
            .Subscribe(value => PostProcessingEnabledInput = value);
            
        _settingsService.WhenAnyValue(x => x.PostProcessingApiUrl)
            .Where(value => value != PostProcessingApiUrlInput)
            .Subscribe(value => PostProcessingApiUrlInput = value);
            
        _settingsService.WhenAnyValue(x => x.PostProcessingModelName)
            .Where(value => value != PostProcessingModelNameInput)
            .Subscribe(value => PostProcessingModelNameInput = value);
            
        _settingsService.WhenAnyValue(x => x.PostProcessingApiKey)
            .Where(value => value != PostProcessingApiKeyInput)
            .Subscribe(value => PostProcessingApiKeyInput = value);
            
        _settingsService.WhenAnyValue(x => x.PostProcessingPrompt)
            .Where(value => value != PostProcessingPromptInput)
            .Subscribe(value => PostProcessingPromptInput = value);
        
        // Propagate input changes back to settings service
        this.WhenAnyValue(x => x.ServerAddressInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.ServerAddress = value);
            
        this.WhenAnyValue(x => x.ApiKeyInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.ApiKey = value);
            
        this.WhenAnyValue(x => x.ModelInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.Model = value);
            
        this.WhenAnyValue(x => x.LanguageInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.Language = value);
            
        this.WhenAnyValue(x => x.PromptInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.Prompt = value);
            
        this.WhenAnyValue(x => x.SaveAudioFileInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.SaveAudioFile = value);
            
        // Only update audio file path when it's valid or save is disabled
        this.WhenAnyValue(x => x.AudioFilePathInput)
            .Where(_ => !this.GetErrors(nameof(AudioFilePathInput)).Cast<string>().Any() || !SaveAudioFileInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.AudioFilePath = value);
            
        this.WhenAnyValue(x => x.OutputTypeInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.OutputType = value);
            
        this.WhenAnyValue(x => x.PostProcessingEnabledInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.PostProcessingEnabled = value);
            
        this.WhenAnyValue(x => x.PostProcessingApiUrlInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.PostProcessingApiUrl = value);
            
        this.WhenAnyValue(x => x.PostProcessingModelNameInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.PostProcessingModelName = value);
            
        this.WhenAnyValue(x => x.PostProcessingApiKeyInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.PostProcessingApiKey = value);
            
        this.WhenAnyValue(x => x.PostProcessingPromptInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.PostProcessingPrompt = value);
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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SelectFolderCommand?.Dispose();
        }
        base.Dispose(disposing);
    }
}
