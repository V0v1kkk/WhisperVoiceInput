using Akka.Actor;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Audio;

#pragma warning disable MEAI001

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for transcribing audio files.
/// </summary>
    public class TranscribingActor : ReceiveActor, IWithUnboundedStash
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly ISpeechToTextClient _speechToTextClient;
        private ICancelable? _timeoutCancelable;
        private string? _currentAudioFilePath;
        public IStash Stash { get; set; } = null!;

        // Internal messages for async result handling on the actor thread
        private sealed record InternalTranscriptionSuccess(string Text, string AudioFile);
        private sealed record InternalTranscriptionFailure(Exception Exception, TranscribeCommand OriginalCommand);

    public TranscribingActor(AppSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;

        // Create Speech-to-Text client via Microsoft.Extensions.AI using the OpenAI provider
        // Mirrors the pattern used in PostProcessorActor for chat
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(_settings.ServerAddress))
        {
            // Expecting ServerAddress to be an OpenAI-compatible endpoint (e.g. http://localhost:port/v1 or https://api.openai.com/v1)
            string address;
            if (!_settings.ServerAddress.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                address = _settings.ServerAddress.TrimEnd('/') + "/v1";
            }
            else
            {
                address = _settings.ServerAddress.TrimEnd('/');
            }
            options.Endpoint = new Uri(address);
        }

        // Fallback to a dummy key if not provided; some OSS local servers ignore the key but require header presence
        var apiKey = string.IsNullOrWhiteSpace(_settings.ApiKey) ? "dummy-api-key" : _settings.ApiKey;
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);

        var audioClient = openAiClient.GetAudioClient(_settings.Model);
        _speechToTextClient = audioClient.AsISpeechToTextClient();

        Ready();
    }

    private void Ready()
    {
        Receive<TranscribeCommand>(HandleTranscribeCommand);
        // Ignore others while idle
        ReceiveAny(_ => { });
    }

    private void Working()
    {
        Receive<InternalTranscriptionSuccess>(HandleInternalSuccess);
        Receive<InternalTranscriptionFailure>(HandleInternalFailure);
        Receive<TranscriptionTimeout>(_ => HandleTimeout());
        // Stash new work while busy
        Receive<TranscribeCommand>(_ =>
        {
            Stash.Stash();
            _logger.Information("Stashed TranscribeCommand while transcribing");
        });
        ReceiveAny(_ => { });
    }

    private void HandleTranscribeCommand(TranscribeCommand cmd)
    {
        try
        {
            _logger.Information("Starting transcription for {FilePath}", cmd.AudioFile);
            _currentAudioFilePath = cmd.AudioFile;

            // Schedule timeout if enabled
            ScheduleTimeoutIfEnabled();
            TranscribeAudioAsync(cmd.AudioFile)
                .PipeTo(Self,
                    success: text => new InternalTranscriptionSuccess(text, cmd.AudioFile),
                    failure: ex => new InternalTranscriptionFailure(ex, cmd));
            Become(Working);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initiate transcription");
            Self.Tell(cmd);
            throw;
        }
    }

    private void HandleInternalSuccess(InternalTranscriptionSuccess msg)
    {
        CancelTimeout();
        _logger.Information("Transcription completed successfully");
        Context.Parent.Tell(new TranscriptionCompletedEvent(msg.Text));
        HandleAudioFileCleanup(msg.AudioFile);
        _currentAudioFilePath = null;
        Become(Ready);
        Stash.UnstashAll();
    }

    private void HandleInternalFailure(InternalTranscriptionFailure msg)
    {
        CancelTimeout();
        _logger.Error(msg.Exception, "Failed to transcribe audio file");
        _currentAudioFilePath = null;
        // Re-enqueue for retry after restart
        Self.Tell(msg.OriginalCommand);
        throw msg.Exception;
    }

    private async Task<string> TranscribeAudioAsync(string audioFilePath)
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Audio file not found", audioFilePath);
        }

        await using var fileStream = File.OpenRead(audioFilePath);

        var options = new SpeechToTextOptions
        {
            ModelId = _settings.Model,
            SpeechLanguage = string.IsNullOrWhiteSpace(_settings.Language) ? null : _settings.Language,
            // Use RawRepresentationFactory to supply OpenAI.Audio.AudioTranscriptionOptions so
            // the internal adapter can pick it up and forward provider-specific settings
            RawRepresentationFactory = string.IsNullOrWhiteSpace(_settings.Prompt) 
                ? null :  
                _ => new AudioTranscriptionOptions
                    {
                        Prompt = _settings.Prompt
                    }
        };

        var sttResponse = await _speechToTextClient.GetTextAsync(fileStream, options);
        if (string.IsNullOrWhiteSpace(sttResponse.Text))
        {
            throw new InvalidOperationException("Received empty transcription result");
        }

        return sttResponse.Text;
    }

    private void HandleAudioFileCleanup(string audioFilePath)
    {
        try
        {
            if (!_settings.SaveAudioFile)
            {
                File.Delete(audioFilePath);
                _logger.Information("Deleted temporary audio file {FilePath}", audioFilePath);
            }
            else if (!string.IsNullOrEmpty(_settings.AudioFilePath))
            {
                // Ensure target directory exists
                var targetDir = Path.GetDirectoryName(_settings.AudioFilePath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                    
                File.Move(audioFilePath, _settings.AudioFilePath, overwrite: true);
                _logger.Information("Moved audio file to {TargetPath}", _settings.AudioFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to handle audio file cleanup for {FilePath}", audioFilePath);
            // Don't throw here - file cleanup failure shouldn't fail the transcription
        }
    }

    private void ScheduleTimeoutIfEnabled()
    {
        CancelTimeout();
        if (_settings.TranscribingTimeoutMinutes > 0)
        {
            var due = TimeSpan.FromMinutes(_settings.TranscribingTimeoutMinutes);
            _timeoutCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(due, Self, new TranscriptionTimeout(), Self);
            _logger.Information("Scheduled transcription timeout in {Minutes} minutes", _settings.TranscribingTimeoutMinutes);
        }
    }

    private void CancelTimeout()
    {
        try
        {
            _timeoutCancelable?.Cancel();
        }
        catch
        {
            _logger.Warning("Failed to cancel transcription timeout");
        }
        _timeoutCancelable = null;
    }

    private void HandleTimeout()
    {
        _logger.Error("Transcription timeout reached, failing actor");
        _currentAudioFilePath = null;
        throw new UserConfiguredTimeoutException("Transcription exceeded configured timeout");
    }

    // Note: deletion of leftover files on failure/timeout is handled by the orchestrator

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "TranscribingActor is restarting due to an exception");
        base.PreRestart(reason, message);
    }
        
    protected override void PostStop()
    {
        CancelTimeout();
        if (_speechToTextClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.PostStop();
    }

    // Response DTO no longer needed; using Microsoft.Extensions.AI models
}