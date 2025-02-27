# WhisperVoiceInput

![WhisperVoiceInput Logo](/WhisperVoiceInput/Assets/lecturer-white.png)

A cross-platform desktop application that records audio and transcribes it to text using OpenAI's Whisper API or compatible services. Perfect for dictation, note-taking, and accessibility.

## Features

- **Audio Recording**: Capture audio from your system's default microphone
- **Speech-to-Text Transcription**: Convert speech to text using OpenAI's Whisper API or compatible services
- **Multiple Output Options**:
  - Copy to clipboard
  - Use `wl-copy` for Wayland systems
  - Type text directly using `ydotool`
- **System Tray Integration**: Monitor recording status with color-coded tray icon
- **Unix Socket Control**: Control the application via command line scripts
- **Configurable Settings**:
  - API endpoint and key
  - Whisper model selection
  - Language preference
  - Custom prompts for better recognition

## Requirements

- .NET 9.0 or higher
- For Wayland clipboard support: `wl-copy`
- For typing output: `ydotool`
- OpenAL compatible sound card/drivers
- OpenAI API key or compatible Whisper API endpoint

## Installation

### From Source

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/WhisperVoiceInput.git
   cd WhisperVoiceInput
   ```

2. Build the application:
   ```bash
   dotnet build -c Release
   ```

3. Run the application:
   ```bash
   dotnet run --project WhisperVoiceInput/WhisperVoiceInput.csproj
   ```

### Pre-built Binaries

Download the latest release from the [Releases](https://github.com/yourusername/WhisperVoiceInput/releases) page.

## Configuration

On first run, the application creates a configuration directory at:
```
~/.config/WhisperVoiceInput/ (Linux/macOS)
%APPDATA%\WhisperVoiceInput\ (Windows)
```

### API Configuration

1. Open the settings window by clicking on the tray icon
2. Enter your OpenAI API key or configure a compatible endpoint
3. Select the Whisper model (default: whisper-large)
4. Set your preferred language (e.g., "en" for English)
5. Optionally add a prompt to guide the transcription

### Output Configuration

Choose your preferred output method:
- **Clipboard**: Standard clipboard (works on most systems)
- **wl-copy**: For Wayland systems (requires `wl-copy` to be installed)
- **ydotool**: Types the text directly (requires `ydotool` to be installed and configured)

## Usage

### GUI Usage

1. Click the tray icon to start/stop recording
2. When recording, the icon turns yellow
3. During transcription processing, the icon turns light blue
4. On success, the icon briefly turns green and the transcribed text is output according to your settings
5. On error, the icon turns red

### Command Line Control

The application can be controlled via Unix socket commands. Two scripts are provided:

#### Simple Toggle Script (toggle.sh)

```bash
#!/bin/bash

MESSAGE="transcribe_toggle"
PIPE_PATH="/tmp/WhisperVoiceInput/pipe"

echo "$MESSAGE" | socat - UNIX-CONNECT:$PIPE_PATH
```

#### Enhanced Toggle Script (transcribe_toggle.sh)

```bash
#!/bin/bash

MESSAGE="transcribe_toggle"
PIPE_PATH="/tmp/WhisperVoiceInput/pipe"

# Check if socat is installed
if ! command -v socat &> /dev/null; then
    echo "Error: socat is not installed. Please install it with your package manager."
    echo "For example: sudo apt install socat"
    exit 1
fi

# Check if the socket exists
if [ ! -S "$PIPE_PATH" ]; then
    echo "Error: Socket $PIPE_PATH does not exist."
    echo "Make sure WhisperVoiceInput is running."
    exit 1
fi

echo "Sending '$MESSAGE' command to WhisperVoiceInput..."
echo "$MESSAGE" | socat - UNIX-CONNECT:$PIPE_PATH
echo "Command sent."
```

Make the scripts executable:
```bash
chmod +x toggle.sh transcribe_toggle.sh
```

Run the script to toggle recording:
```bash
./toggle.sh
```

## Keyboard Shortcuts

You can bind the toggle script to a keyboard shortcut in your desktop environment for quick access:

### GNOME Example:
```bash
gsettings set org.gnome.settings-daemon.plugins.media-keys custom-keybindings "['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/']"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/ name "Toggle WhisperVoiceInput"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/ command "/path/to/toggle.sh"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/ binding "<Ctrl><Alt>w"
```

### KDE Example:
1. System Settings > Shortcuts > Custom Shortcuts
2. Add a new shortcut
3. Set the command to `/path/to/toggle.sh`
4. Assign a keyboard shortcut

## Troubleshooting

### Recording Issues

- Ensure your microphone is properly connected and set as the default input device
- Check system permissions for microphone access
- Verify OpenAL is properly installed and configured

### Transcription Issues

- Verify your API key is correct
- Check your internet connection
- Ensure the server address is correct
- Try a different Whisper model (smaller models may be faster but less accurate)

### Output Issues

- For clipboard issues, try a different output method
- For `wl-copy`, ensure it's installed: `sudo apt install wl-clipboard`
- For `ydotool`, ensure it's installed and properly configured

### Socket Control Issues

- Ensure the application is running
- Check if the socket file exists at `/tmp/WhisperVoiceInput`
- Verify `socat` is installed: `sudo apt install socat`

## Logs

Logs are stored in:
```
~/.config/WhisperVoiceInput/logs/ (Linux/macOS)
%APPDATA%\WhisperVoiceInput\logs\ (Windows)
```

## License

[MIT License](LICENSE)

## Acknowledgements

- [OpenAI Whisper](https://github.com/openai/whisper) - Speech recognition model
- [Avalonia UI](https://avaloniaui.net/) - Cross-platform UI framework
- [NAudio](https://github.com/naudio/NAudio) - Audio library for .NET
- [OpenTK.OpenAL](https://github.com/opentk/opentk) - OpenAL bindings for .NET