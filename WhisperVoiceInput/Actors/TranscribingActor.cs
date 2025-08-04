using Akka.Actor;
using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using WhisperVoiceInput.Messages;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Actors;

/// <summary>
/// Actor responsible for transcribing audio files.
/// </summary>
public class TranscribingActor : ReceiveActor
{
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public TranscribingActor(AppSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
        _httpClient = new HttpClient();
            
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
        }
            
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        ReceiveAsync<TranscribeCommand>(HandleTranscribeCommand);
    }

    private async Task HandleTranscribeCommand(TranscribeCommand cmd)
    {
        try
        {
            _logger.Information("Starting transcription for {FilePath}", cmd.AudioFile);
                
            var result = await TranscribeAudioAsync(cmd.AudioFile);
                
            _logger.Information("Transcription completed successfully");
                
            // Send result first
            Sender.Tell(new TranscriptionCompletedEvent(result));
                
            // Then cleanup the file after successful transcription
            HandleAudioFileCleanup(cmd.AudioFile);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to transcribe audio file");
                
            // Self-tell for retry after restart - file remains intact
            Self.Tell(cmd);
            throw;
        }
    }

    private async Task<string> TranscribeAudioAsync(string audioFilePath)
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Audio file not found", audioFilePath);
        }

        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(audioFilePath);
        using var streamContent = new StreamContent(fileStream);
            
        form.Add(streamContent, "file", Path.GetFileName(audioFilePath));
        form.Add(new StringContent(_settings.Model), "model");
            
        if (!string.IsNullOrEmpty(_settings.Language))
        {
            form.Add(new StringContent(_settings.Language), "language");
        }

        if (!string.IsNullOrEmpty(_settings.Prompt))
        {
            form.Add(new StringContent(_settings.Prompt), "prompt");
        }

        var response = await _httpClient.PostAsync(
            $"{_settings.ServerAddress.TrimEnd('/')}/v1/audio/transcriptions",
            form);

        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TranscriptionResponse>(jsonResponse, _jsonOptions);

        if (result?.Text == null)
        {
            throw new InvalidOperationException("Received empty transcription result");
        }

        return result.Text;
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

    protected override void PreRestart(Exception reason, object message)
    {
        _logger.Warning(reason, "TranscribingActor is restarting due to an exception");
        base.PreRestart(reason, message);
    }
        
    protected override void PostStop()
    {
        _httpClient.Dispose();
        base.PostStop();
    }

    private class TranscriptionResponse
    {
        public string? Text { get; set; }
    }
}