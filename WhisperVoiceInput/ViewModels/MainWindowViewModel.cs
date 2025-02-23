using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private readonly string _settingsPath;

    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _model = "whisper-large";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _saveAudioFile;

    [ObservableProperty]
    private string _audioFilePath = string.Empty;

    [ObservableProperty]
    private bool _useWlCopy;

    [ObservableProperty]
    private bool _isSaving;

    public MainWindowViewModel(ILogger logger)
    {
        _logger = logger;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WhisperVoiceInput",
            "settings.json");

        LoadSettings();
    }

    partial void OnServerAddressChanged(string value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnApiKeyChanged(string value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnModelChanged(string value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnLanguageChanged(string value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnPromptChanged(string value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnSaveAudioFileChanged(bool value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnAudioFilePathChanged(string value) => SaveSettingsAsync().ConfigureAwait(false);
    partial void OnUseWlCopyChanged(bool value) => SaveSettingsAsync().ConfigureAwait(false);

    private async Task SaveSettingsAsync()
    {
        if (IsSaving) return;

        try
        {
            IsSaving = true;

            var settings = new AppSettings
            {
                ServerAddress = ServerAddress,
                ApiKey = ApiKey,
                Model = Model,
                Language = Language,
                Prompt = Prompt,
                SaveAudioFile = SaveAudioFile,
                AudioFilePath = AudioFilePath,
                UseWlCopy = UseWlCopy
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_settingsPath, json);
            _logger.Information("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
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
                    UseWlCopy = settings.UseWlCopy;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings");
        }
    }
}