using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
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
    
    // Mutable backing fields for two-way binding
    private string _serverAddressInput = string.Empty;
    private string _apiKeyInput = string.Empty;
    private string _modelInput = string.Empty;
    private string _languageInput = string.Empty;
    private string _promptInput = string.Empty;
    private bool _saveAudioFileInput;
    private string _audioFilePathInput = string.Empty;
    private ResultOutputType _outputTypeInput;

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
        _settingsService.SetSaveAudioFile(SaveAudioFileInput);
        _settingsService.SetAudioFilePath(AudioFilePathInput);
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
        _saveSettingsCommand.Dispose();
    }
}