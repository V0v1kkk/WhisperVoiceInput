using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using WhisperVoiceInput.Models;

namespace WhisperVoiceInput.Services;

public class TranscriptionService : IDisposable
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    
    private readonly JsonSerializerOptions _jsonOptions;

    public TranscriptionService(ILogger logger, AppSettings settings)
    {
        _logger = logger;
        _settings = settings;
        _httpClient = new HttpClient();
        
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
        }
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<string> TranscribeAudioAsync(string audioFilePath)
    {
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Audio file not found", audioFilePath);
        }

        try
        {
            _logger.Information("Starting transcription for {FilePath}", audioFilePath);

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

            _logger.Information("Transcription completed successfully");
            return result.Text;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to transcribe audio file");
            throw;
        }
        finally
        {
            if (!_settings.SaveAudioFile)
            {
                try
                {
                    File.Delete(audioFilePath);
                    _logger.Information("Deleted temporary audio file {FilePath}", audioFilePath);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to delete temporary audio file {FilePath}", audioFilePath);
                }
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private class TranscriptionResponse
    {
        public string? Text { get; set; }
    }
}