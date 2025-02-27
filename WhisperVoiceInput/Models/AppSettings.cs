using System;

namespace WhisperVoiceInput.Models;

public record AppSettings
{
    public string ServerAddress { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "whisper-large";
    public string Language { get; init; } = "en";
    public string Prompt { get; init; } = string.Empty;
    
    public bool SaveAudioFile { get; init; }
    public string AudioFilePath { get; init; } = string.Empty;
    
    
    public ResultOutputType OutputType { get; init; } = ResultOutputType.ClipboardAvaloniaApi;
}