using System;
using System.Text.Json.Serialization;

namespace WhisperVoiceInput.Models;

public enum ResultOutputType
{
    Clipboard,
    WlCopy,
    YdotoolType
}

public class AppSettings
{
    // Connection Settings
    public string ServerAddress { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "whisper-large";
    public string Language { get; set; } = "en";
    public string Prompt { get; set; } = string.Empty;

    // Audio Settings
    public bool SaveAudioFile { get; set; }
    public string AudioFilePath { get; set; } = string.Empty;

    // Result Settings
    public ResultOutputType OutputType { get; set; } = ResultOutputType.Clipboard;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(ServerAddress) &&
                          (!SaveAudioFile || !string.IsNullOrWhiteSpace(AudioFilePath));
}