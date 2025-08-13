using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenTK.Audio.OpenAL;
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
    private const string DefaultDeviceLabel = "System default";
    private const string UnavailableSuffix = " (Unavailable)";
    
    // Two-way bindable input properties for UI using source generation
    [Reactive] public partial string ServerAddressInput { get; set; } = string.Empty;
    [Reactive] public partial string ApiKeyInput { get; set; } = string.Empty;
    [Reactive] public partial string ModelInput { get; set; } = string.Empty;
    [Reactive] public partial string LanguageInput { get; set; } = string.Empty;
    [Reactive] public partial string PromptInput { get; set; } = string.Empty;
    [Reactive] public partial bool SaveAudioFileInput { get; set; }
    [Reactive] public partial string AudioFilePathInput { get; set; } = string.Empty;
    [Reactive] public partial string PreferredCaptureDeviceInput { get; set; } = string.Empty;
    [Reactive] public partial string SelectedCaptureDeviceItem { get; set; } = DefaultDeviceLabel;
    [Reactive] public partial string[] AvailableCaptureDevices { get; set; } = Array.Empty<string>();
    [Reactive] public partial ResultOutputType OutputTypeInput { get; set; }
    [Reactive] public partial bool PostProcessingEnabledInput { get; set; }
    [Reactive] public partial string PostProcessingApiUrlInput { get; set; } = string.Empty;
    [Reactive] public partial string PostProcessingModelNameInput { get; set; } = string.Empty;
    [Reactive] public partial string PostProcessingApiKeyInput { get; set; } = string.Empty;
    [Reactive] public partial string PostProcessingPromptInput { get; set; } = string.Empty;
    [Reactive] public partial bool DatasetSavingEnabledInput { get; set; }
    [Reactive] public partial string DatasetFilePathInput { get; set; } = string.Empty;
	[Reactive] public partial int RecordingTimeoutMinutesInput { get; set; }
	[Reactive] public partial int TranscribingTimeoutMinutesInput { get; set; }
	[Reactive] public partial int PostProcessingTimeoutMinutesInput { get; set; }
	[Reactive] public partial bool RecordingTimeoutEnabledInput { get; set; }
	[Reactive] public partial bool TranscribingTimeoutEnabledInput { get; set; }
	[Reactive] public partial bool PostProcessingTimeoutEnabledInput { get; set; }
    
    public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectDatasetFileCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCaptureDevicesCommand { get; }

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
        PreferredCaptureDeviceInput = _settingsService.PreferredCaptureDevice;
        OutputTypeInput = _settingsService.OutputType;
        PostProcessingEnabledInput = _settingsService.PostProcessingEnabled;
        PostProcessingApiUrlInput = _settingsService.PostProcessingApiUrl;
        PostProcessingModelNameInput = _settingsService.PostProcessingModelName;
        PostProcessingApiKeyInput = _settingsService.PostProcessingApiKey;
        PostProcessingPromptInput = _settingsService.PostProcessingPrompt;
		// New dataset saving
        DatasetSavingEnabledInput = _settingsService.DatasetSavingEnabled;
        DatasetFilePathInput = _settingsService.DatasetFilePath;
		// Timeouts (minutes; <=0 disabled)
		RecordingTimeoutMinutesInput = _settingsService.RecordingTimeoutMinutes > 0 ? _settingsService.RecordingTimeoutMinutes : 1;
		TranscribingTimeoutMinutesInput = _settingsService.TranscribingTimeoutMinutes > 0 ? _settingsService.TranscribingTimeoutMinutes : 1;
		PostProcessingTimeoutMinutesInput = _settingsService.PostProcessingTimeoutMinutes > 0 ? _settingsService.PostProcessingTimeoutMinutes : 1;
		RecordingTimeoutEnabledInput = _settingsService.RecordingTimeoutMinutes > 0;
		TranscribingTimeoutEnabledInput = _settingsService.TranscribingTimeoutMinutes > 0;
		PostProcessingTimeoutEnabledInput = _settingsService.PostProcessingTimeoutMinutes > 0;
        
		SetupValidationRules();
		// Initialize capture device list BEFORE wiring Selected->Preferred mapping
		InitializeCaptureDeviceListSkeleton();
		// Now wire up reactive synchronization
		SetupSettingsSynchronization();
        
        // Set up folder selection command
        SelectFolderCommand = ReactiveCommand.CreateFromTask(
            SelectFolderAsync,
            this.WhenAnyValue(x => x.SaveAudioFileInput),
            AvaloniaScheduler.Instance);

        // Select dataset file command
        SelectDatasetFileCommand = ReactiveCommand.CreateFromTask(
            SelectDatasetFileAsync,
            this.WhenAnyValue(x => x.DatasetSavingEnabledInput),
            AvaloniaScheduler.Instance);

        // Refresh capture devices on demand
        RefreshCaptureDevicesCommand = ReactiveCommand.CreateFromTask(
            RefreshCaptureDevicesAsync,
            outputScheduler: AvaloniaScheduler.Instance);
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

        // Dataset file path validation
        var datasetPathValidation = this.WhenAnyValue(
                x => x.DatasetSavingEnabledInput,
                x => x.DatasetFilePathInput,
                (enabled, path) => new { enabled, path })
            .Select(state =>
            {
                if (!state.enabled)
                    return ValidationState.Valid;

                if (string.IsNullOrWhiteSpace(state.path))
                    return new ValidationState(false, "Dataset file is required when dataset saving is enabled");

                try
                {
                    var dir = Path.GetDirectoryName(state.path);
                    if (string.IsNullOrEmpty(dir))
                        return new ValidationState(false, "Invalid dataset file path");
                    if (!Directory.Exists(dir))
                        return new ValidationState(false, "Directory for dataset file does not exist");

                    // Try open for append and close immediately
                    using var _ = File.Open(state.path, FileMode.Append, FileAccess.Write, FileShare.Read);
                }
                catch (UnauthorizedAccessException)
                {
                    return new ValidationState(false, "No permission to write dataset file");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error validating dataset path: {Path}", state.path);
                    return new ValidationState(false, $"Error validating dataset path: {ex.Message}");
                }

                return ValidationState.Valid;
            });
        this.ValidationRule(vm => vm.DatasetFilePathInput, datasetPathValidation);
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
        
        _settingsService.WhenAnyValue(x => x.PreferredCaptureDevice)
            .Where(value => value != PreferredCaptureDeviceInput)
            .Subscribe(value => PreferredCaptureDeviceInput = value);
            
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
        
        _settingsService.WhenAnyValue(x => x.DatasetSavingEnabled)
            .Where(value => value != DatasetSavingEnabledInput)
            .Subscribe(value => DatasetSavingEnabledInput = value);

		_settingsService.WhenAnyValue(x => x.DatasetFilePath)
            .Where(value => value != DatasetFilePathInput)
            .Subscribe(value => DatasetFilePathInput = value);

		// Map settings minutes -> enabled flags and minutes inputs
		_settingsService.WhenAnyValue(x => x.RecordingTimeoutMinutes)
			.Subscribe(value =>
			{
				var enabled = value > 0;
				if (RecordingTimeoutEnabledInput != enabled) RecordingTimeoutEnabledInput = enabled;
				if (enabled && RecordingTimeoutMinutesInput != value) RecordingTimeoutMinutesInput = value;
			});

		_settingsService.WhenAnyValue(x => x.TranscribingTimeoutMinutes)
			.Subscribe(value =>
			{
				var enabled = value > 0;
				if (TranscribingTimeoutEnabledInput != enabled) TranscribingTimeoutEnabledInput = enabled;
				if (enabled && TranscribingTimeoutMinutesInput != value) TranscribingTimeoutMinutesInput = value;
			});

		_settingsService.WhenAnyValue(x => x.PostProcessingTimeoutMinutes)
			.Subscribe(value =>
			{
				var enabled = value > 0;
				if (PostProcessingTimeoutEnabledInput != enabled) PostProcessingTimeoutEnabledInput = enabled;
				if (enabled && PostProcessingTimeoutMinutesInput != value) PostProcessingTimeoutMinutesInput = value;
			});

        // (Removed duplicate settings->VM minutes bindings; handled above with enabled sync)
        
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
        
        this.WhenAnyValue(x => x.PreferredCaptureDeviceInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.PreferredCaptureDevice = value);

        // Map selected item to stored preferred device (empty means system default)
        this.WhenAnyValue(x => x.SelectedCaptureDeviceItem)
            .DistinctUntilChanged()
            .Subscribe(selected =>
            {
                if (string.Equals(selected, DefaultDeviceLabel, StringComparison.Ordinal))
                {
                    PreferredCaptureDeviceInput = string.Empty;
                }
                else if (!string.IsNullOrEmpty(selected))
                {
                    var clean = selected.Replace(UnavailableSuffix, string.Empty, StringComparison.Ordinal);
                    PreferredCaptureDeviceInput = clean;
                }
            });
            
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
        
        this.WhenAnyValue(x => x.DatasetSavingEnabledInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.DatasetSavingEnabled = value);
        
		this.WhenAnyValue(x => x.DatasetFilePathInput)
            .DistinctUntilChanged()
            .Subscribe(value => _settingsService.DatasetFilePath = value);

		// Timeouts: propagate VM -> settings with enable/disable mapping
		this.WhenAnyValue(x => x.RecordingTimeoutEnabledInput)
			.DistinctUntilChanged()
			.Subscribe(enabled => OnTimeoutToggleChanged(
				enabled,
				() => RecordingTimeoutMinutesInput,
				v => RecordingTimeoutMinutesInput = v,
				v => _settingsService.RecordingTimeoutMinutes = v));

		this.WhenAnyValue(x => x.TranscribingTimeoutEnabledInput)
			.DistinctUntilChanged()
			.Subscribe(enabled => OnTimeoutToggleChanged(
				enabled,
				() => TranscribingTimeoutMinutesInput,
				v => TranscribingTimeoutMinutesInput = v,
				v => _settingsService.TranscribingTimeoutMinutes = v));

		this.WhenAnyValue(x => x.PostProcessingTimeoutEnabledInput)
			.DistinctUntilChanged()
			.Subscribe(enabled => OnTimeoutToggleChanged(
				enabled,
				() => PostProcessingTimeoutMinutesInput,
				v => PostProcessingTimeoutMinutesInput = v,
				v => _settingsService.PostProcessingTimeoutMinutes = v));

		this.WhenAnyValue(x => x.RecordingTimeoutMinutesInput)
			.DistinctUntilChanged()
			.Subscribe(value => OnTimeoutMinutesChanged(
				value,
				RecordingTimeoutEnabledInput,
				v => RecordingTimeoutMinutesInput = v,
				v => _settingsService.RecordingTimeoutMinutes = v));

		this.WhenAnyValue(x => x.TranscribingTimeoutMinutesInput)
			.DistinctUntilChanged()
			.Subscribe(value => OnTimeoutMinutesChanged(
				value,
				TranscribingTimeoutEnabledInput,
				v => TranscribingTimeoutMinutesInput = v,
				v => _settingsService.TranscribingTimeoutMinutes = v));

		this.WhenAnyValue(x => x.PostProcessingTimeoutMinutesInput)
			.DistinctUntilChanged()
			.Subscribe(value => OnTimeoutMinutesChanged(
				value,
				PostProcessingTimeoutEnabledInput,
				v => PostProcessingTimeoutMinutesInput = v,
				v => _settingsService.PostProcessingTimeoutMinutes = v));
        // (Removed duplicate VM->settings minute propagation; handled by helpers above)
    }

    private static int ClampTimeout(int value) => value < 1 ? 1 : value;

    private void OnTimeoutToggleChanged(
        bool enabled,
        Func<int> getVmMinutes,
        Action<int> setVmMinutes,
        Action<int> setSettingsMinutes)
    {
        if (enabled)
        {
            var v = ClampTimeout(getVmMinutes());
            if (v != getVmMinutes()) setVmMinutes(v);
            setSettingsMinutes(v);
        }
        else
        {
            setSettingsMinutes(-1);
        }
    }

    private void OnTimeoutMinutesChanged(
        int value,
        bool enabled,
        Action<int> setVmMinutes,
        Action<int> setSettingsMinutes)
    {
        if (!enabled) return;
        var v = ClampTimeout(value);
        if (v != value) setVmMinutes(v);
        setSettingsMinutes(v);
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

    private async Task SelectDatasetFileAsync()
    {
        try
        {
            var mainWindow = Application.Current?.ApplicationLifetime is 
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
            if (mainWindow == null)
            {
                _logger.Error("Could not get main window for save file dialog");
                return;
            }

            var options = new FilePickerSaveOptions
            {
                Title = "Select Dataset File",
                SuggestedFileName = string.IsNullOrWhiteSpace(DatasetFilePathInput)
                    ? "dataset.txt"
                    : Path.GetFileName(DatasetFilePathInput),
                ShowOverwritePrompt = false,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
                }
            };

            var result = await mainWindow.StorageProvider.SaveFilePickerAsync(options);
            if (result != null)
            {
                var path = result.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    DatasetFilePathInput = path;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error selecting dataset file");
        }
    }

    private void InitializeCaptureDeviceListSkeleton()
    {
        var items = new List<string> { DefaultDeviceLabel };

        if (!string.IsNullOrWhiteSpace(PreferredCaptureDeviceInput))
        {
            items.Add(PreferredCaptureDeviceInput);
            SelectedCaptureDeviceItem = PreferredCaptureDeviceInput;
        }
        else
        {
            SelectedCaptureDeviceItem = DefaultDeviceLabel;
        }

        AvailableCaptureDevices = items.ToArray();
    }

    private async Task RefreshCaptureDevicesAsync()
    {
        try
        {
            var devices = new List<string> { DefaultDeviceLabel };

            IEnumerable<string>? enumerated = null;
            try
            {
                enumerated = ALC.GetString(ALDevice.Null, AlcGetStringList.CaptureDeviceSpecifier);
                // Include extended devices if supported
                IEnumerable<string>? allDevices = null;
                try { allDevices = ALC.GetString(ALDevice.Null, AlcGetStringList.AllDevicesSpecifier); }
                catch { }
                if (allDevices != null)
                {
                    enumerated = enumerated != null ? enumerated.Concat(allDevices) : allDevices;
                }
            }
            catch
            {
                try
                {
                    enumerated = ALC.GetStringList(GetEnumerationStringList.CaptureDeviceSpecifier);
                }
                catch
                {
                    // Ignore; will fall back to default-only list
                }
            }

            if (enumerated != null)
            {
                foreach (var dev in enumerated)
                {
                    if (!string.IsNullOrWhiteSpace(dev))
                        devices.Add(dev);
                }
            }

            devices = devices.Distinct(StringComparer.Ordinal).ToList();

            // If saved preferred is not in the list, show with marker
            if (!string.IsNullOrWhiteSpace(PreferredCaptureDeviceInput) &&
                !devices.Contains(PreferredCaptureDeviceInput, StringComparer.Ordinal))
            {
                devices.Add(PreferredCaptureDeviceInput + UnavailableSuffix);
            }

            AvailableCaptureDevices = devices.ToArray();

            // Select appropriate item
            if (string.IsNullOrWhiteSpace(PreferredCaptureDeviceInput))
            {
                SelectedCaptureDeviceItem = DefaultDeviceLabel;
            }
            else
            {
                var exact = PreferredCaptureDeviceInput;
                if (devices.Contains(exact, StringComparer.Ordinal))
                {
                    SelectedCaptureDeviceItem = exact;
                }
                else
                {
                    SelectedCaptureDeviceItem = exact + UnavailableSuffix;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh capture devices");
        }
    }
    
    // Clean up resources
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SelectFolderCommand?.Dispose();
            SelectDatasetFileCommand?.Dispose();
            RefreshCaptureDevicesCommand?.Dispose();
        }
        base.Dispose(disposing);
    }
}
