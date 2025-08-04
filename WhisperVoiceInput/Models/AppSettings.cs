using System;

namespace WhisperVoiceInput.Models;

public record AppSettings
{
    // Whisper Transcription Settings
    public string ServerAddress { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "whisper-large";
    public string Language { get; init; } = "en";
    public string Prompt { get; init; } = string.Empty;
    
    // Audio Recording Settings
    public bool SaveAudioFile { get; init; }
    public string AudioFilePath { get; init; } = string.Empty;
    
    // Output Settings
    public ResultOutputType OutputType { get; init; } = ResultOutputType.ClipboardAvaloniaApi;
    
    // Post-Processing Settings
    public bool PostProcessingEnabled { get; init; } = false;
    public string PostProcessingApiUrl { get; init; } = "https://api.openai.com/v1/chat/completions";
    public string PostProcessingModelName { get; init; } = "gpt-3.5-turbo";
    public string PostProcessingApiKey { get; init; } = string.Empty;
    public string PostProcessingPrompt { get; init; } = "You are a helpful assistant that improves transcribed text. " +
        "Fix any obvious transcription errors, improve punctuation and capitalization, " +
        "but preserve the original meaning and content. " +
        "Return only the corrected text without any additional commentary.";
}
