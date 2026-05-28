# WhisperVoiceInput

![WhisperVoiceInput Logo](/WhisperVoiceInput/Assets/lecturer-white.png)

A cross-platform desktop application that records audio and transcribes it to text using OpenAI's Whisper API or compatible services. 
Perfect for dictation, note-taking, and accessibility.

## Disclaimer

The project is a tool for fulfilling my personal needs. 
I use Linux + Wayland and the tool has been tested only on this platform.

It supports only OpenAI compatible Whisper API.
Supported output methods you can find down below.

Feel free to fork the project and make it compatible with your needs.
PRs are welcome.

## What's new - Audio backend migration to SoundFlow (May 2026)

Replaced `OpenTK.Audio.OpenAL` + `NAudio` + `NAudio.Lame.CrossPlatform` with [SoundFlow](https://github.com/LSXPrime/SoundFlow) + `SoundFlow.Codecs.FFMpeg`.

Key changes:
- No more system-level dependencies for audio capture or MP3 encoding (no OpenAL, no LAME)
- All native libraries (miniaudio + FFmpeg) are bundled inside the application binary
- Dramatically simplified audio recording code (event-driven instead of manual polling loop)
- Cross-platform MP3 encoding works out of the box on all platforms

## What’s new (major refactor) - 10.08.2025

The backend was rewritten to an actor-based architecture using Akka.NET and the pipeline was extended with optional AI post‑processing and dataset saving. 
Comprehensive unit and integration tests were added.

Key changes:
- Akka.NET actor model with a supervised pipeline and clear FSM states
- Frozen settings per session, stashing updates while processing
- Observer actor exposes a reactive stream for UI state updates
- Optional post‑processing via Microsoft.Extensions.AI (OpenAI‑compatible)
- Optional dataset saving (original → processed pairs) when post‑processing is enabled
- Robust error handling and retries per actor (configurable policy)
- Tests: FSM/unit, pipeline integration with deterministic timing, and error scenarios

## Features

- Audio Recording: Capture audio from selected microphone (system default or user‑selected)
- Speech-to-Text Transcription: Convert speech to text using OpenAI's Whisper API or compatible services
- Multiple Output Options:
  - Copy to clipboard (Avalonia clipboard; splash workaround due to platform issue)
  - Use `wl-copy` for Wayland systems
  - Type text directly using `ydotool`
  - Type text directly using `wtype`
  - Insert via Wayland IME (`zwp_input_method_v2` protocol) with configurable fallback and optional dual-output mode
  - None (skip output; useful when a completion hook handles the result)
- System Tray Integration: Monitor recording status with color-coded tray icon
- Unix Socket Control: Control the application via command line scripts
- Configurable Settings:
  - API endpoint and key
  - Whisper model selection
  - Language preference
  - Custom prompts for better recognition
- Optional Post‑Processing: Improve text with an LLM via Microsoft.Extensions.AI
- Optional Dataset Saving (for ML datasets): Append original and processed pairs when post‑processing is enabled (see Configuration → Dataset Saving)
- Completion Hook (optional): Run an arbitrary shell command after transcription/post‑processing succeeds (see Configuration → Completion Hook)
- Safety Timeouts (optional): Hard cut‑offs for Recording, Transcribing, Post‑Processing steps

## Roadmap

- [ ] Remove the splash screen after clipboard issue is fixed
- [ ] Add realtime transcription (streaming API)
- [x] Test MacOS support and update docs
- [x] Add shortcut support
- [x] Add more post-processing options

## Requirements

- **For Linux:** `socat` (for socket control)
- **For Wayland clipboard support:** `wl-copy`
- **For typing output:** `ydotool` or `wtype`
- **For Wayland IME output:** a compositor supporting `zwp_input_method_manager_v2` (sway, niri, Hyprland, etc.)
- **OpenAI API key** or compatible Whisper API endpoint
  - OpenAI base URL: `https://api.openai.com`
  - OpenAI model name: `whisper-1`
  - Self-hosted servers often use Whisper Large variants (e.g., faster‑whisper). The UI defaults use a large model name. Adjust to `whisper-1` if you call OpenAI directly.

> **Note:** Audio capture and MP3 encoding are handled by bundled native libraries (miniaudio + FFmpeg via SoundFlow). No system-level audio dependencies (OpenAL, LAME) are required.

## Installation

### Prerequisites

- .NET 9.0 SDK (for building from source)

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

Download the latest release from the [Releases](https://github.com/V0v1kkk/WhisperVoiceInput/releases) page.

### macOS Installation

The macOS release is packaged as a `.app` bundle inside a zip archive. Since the app is not signed with an Apple Developer certificate, macOS Gatekeeper will block it by default.

To install and run:

1. Download and extract the `.zip` for your architecture (`osx-arm64` for Apple Silicon, `osx-x64` for Intel)
2. Move `WhisperVoiceInput.app` to your Applications folder (or any preferred location)
3. Remove the quarantine attribute (required, since the app is not notarized):
   ```bash
   xattr -rd com.apple.quarantine /Applications/WhisperVoiceInput.app
   ```
4. Double-click `WhisperVoiceInput.app` to launch

On macOS versions before 15.1, you can alternatively right-click the app, select "Open", and confirm in the dialog. On macOS 15.1+ the `xattr` command above is the reliable method.

## Configuration

On first run, the application creates a configuration directory at:
```
~/.config/WhisperVoiceInput/ (Linux/macOS)
%APPDATA%\WhisperVoiceInput\ (Windows)
```

### API Configuration

1. Open the settings window by clicking on the tray icon
2. Enter your OpenAI API key or configure a compatible endpoint
3. Select the Whisper model
   - OpenAI: `whisper-1`
   - Self-hosted: a Faster-Whisper model name (e.g., `whisper-large-v3`)
4. Set your preferred language (e.g., "en")
5. Optionally add a prompt to guide the transcription

### Audio Input Device Selection

- In Settings → Audio Settings, use the “Input Device” dropdown to choose a microphone:
  - `System default` uses your OS default input device.
  - Or select a specific device from the list.
- Click “Refresh” to enumerate devices on demand (keeps startup/settings opening light‑weight).
  - Under the hood, the app queries capture devices via the miniaudio backend (WASAPI/CoreAudio/ALSA/PulseAudio).
- The selection is saved as a plain string setting (`PreferredCaptureDevice`).
  - Empty value means `System default`.
- If the preferred device is unavailable at runtime, the recorder automatically falls back to the system default.

### Audio Format

Choose between MP3 (compressed) and WAV (uncompressed) format:
- **MP3** (default): Smaller temporary files, works on all platforms (encoding handled by bundled FFmpeg)
- **WAV**: Uncompressed format, slightly larger temporary files (automatically cleaned up)
- No quality difference for Whisper API recognition

To switch format, use the "Audio Format" toggle in Settings.

### Output Configuration

Choose your preferred output method:
- Clipboard (Avalonia API)
- wl-copy (Wayland)
- ydotool (types the text)
- wtype (types the text)
- Wayland IME — direct text insertion via the `zwp_input_method_v2` protocol. Works on wlroots-based compositors (sway, niri, Hyprland). When IME commit is not possible (no focused text field, unsupported compositor), the app uses a configurable fallback method. The fallback defaults to `wl-copy` and can be changed in the UI to any other output method (including None). An optional "Also run fallback on success" checkbox makes the fallback execute unconditionally — useful for keeping text in the clipboard for later reuse even when IME insertion succeeds.
- None (do nothing — skip output entirely; useful when a completion hook handles the result)

### Post-Processing (optional)

- Enable to improve transcriptions via Microsoft.Extensions.AI
- Endpoint and model are OpenAI‑compatible (OpenAI or local LLM gateways)
- Defaults in the app may point to a local endpoint and model (e.g., Ollama `http://localhost:11434` with `llama3.2`); adjust as needed
- Provide API key if your endpoint requires it

### Safety Timeouts (optional)

- Three independent limits in minutes: Recording, Transcribing, Post‑Processing
- Each timeout can be enabled via a toggle and a minutes spinner (minimum 1 minute)
- Semantics:
  - Value > 0: timeout is enabled; the corresponding actor schedules a self‑timeout message
  - Value ≤ 0 (internally stored as -1): timeout is disabled
- Behavior on timeout:
  - The actor throws `UserConfiguredTimeoutException` which is treated as unrecoverable by supervision (no retries)
  - For Recording and Transcribing, the current audio file is deleted to avoid leaving temporary files behind

### Dataset Saving (optional)

Build your own training datasets from the pipeline output.

- Availability: Only works when Post‑Processing is enabled
- Format per entry:
  ```
  <original text>
  -
  <processed text>
  ---
  ```
- How to enable:
  1. In Settings, enable Post‑Processing
  2. Turn on "Save dataset"
  3. Choose the target file path (created if missing)
  4. Run the pipeline; after post‑processing, an entry is appended asynchronously
- Notes:
  - Appends are non-blocking and won’t stall the UI
  - Success and errors are logged
  - Ensure the chosen location is writable by your user

### Completion Hook (optional)

Run an arbitrary shell command after the transcription (or post‑processing, if enabled) succeeds. The hook runs in the background (fire‑and‑forget) and does not block the pipeline.

- **Shell detection**: The system shell is auto‑detected — `$SHELL` on Linux/macOS (falls back to `/bin/sh`), `COMSPEC` on Windows (falls back to `cmd.exe`). No manual configuration needed.
- **Placeholder**: Use `{{RESULT}}` anywhere in the command to insert the transcription result. The value is automatically shell‑escaped (POSIX single‑quote style) to prevent injection.
- **How to enable**:
  1. In Settings → Hooks, toggle the Completion Hook on
  2. Enter a shell command (e.g., `notify-send -t 3000 'Transcription complete'`)
  3. Optionally include `{{RESULT}}` to pass the transcribed text to the command
- **Example — desktop notification with result on Linux**:
  ```
  notify-send --urgency=low -i info -a "WhisperVoiceInput" -t 3000 "Transcription finished" {{RESULT}}
  ```
- **Notes**:
  - The hook only fires on successful completion, not on errors
  - Errors during hook execution are logged but do not affect the pipeline
  - Combine with the "None" output option if you want the hook to be the sole consumer of the result

### Self-Hosted Whisper API

I personally use [Speaches](https://github.com/speaches-ai/speaches) as a self-hosted Whisper API.

An example of docker-compose file for GPU enhanced version of Speaches:
```yaml
  speaches:
    image: ghcr.io/speaches-ai/speaches:0.7.0-cuda # https://github.com/speaches-ai/speaches/pkgs/container/speaches/versions?filters%5Bversion_type%5D=tagged
    container_name: speaches
    restart: unless-stopped
    ports:
      - "1264:8000"
    volumes:
      - ./speaches_cache:/home/ubuntu/.cache/huggingface/hub
    environment:
      - ENABLE_UI=false
      - WHISPER__TTL=-1 # default TTL is 300 (5min), -1 to disable, 0 to unload directly, 43200=12h
      - WHISPER__INFERENCE_DEVICE=cuda
      - WHISPER__COMPUTE_TYPE=float16
      - WHISPER__MODEL=deepdml/faster-whisper-large-v3-turbo-ct2 # uses ~2.5Gb VRAM in CUDA version
      #- WHISPER__MODEL=Systran/faster-whisper-large-v3
      - WHISPER__DEVICE_INDEX=1
      - ALLOW_ORIGINS=[ "*", "app://obsidian.md" ]
      - API_KEY=sk-1234567890
      - LOOPBACK_HOST_URL=yourdomain.com
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
```

## Usage

### GUI Usage

1. Click the tray icon to start/stop recording
2. When recording, the icon turns yellow
3. During transcription/post‑processing/saving, the icon turns light blue
4. On success, the icon briefly turns green and the text is output per your settings
5. On error, the icon turns red and a tooltip shows details

### Command Line Control

The application can be controlled via a Unix socket. Two scripts are provided in the repo root:

- `transcribe_toggle_simplified.sh` (simple)
- `transcribe_toggle.sh` (enhanced checks)

Make the scripts executable:
```bash
chmod +x transcribe_toggle_simplified.sh transcribe_toggle.sh
```

Run to toggle recording:
```bash
./transcribe_toggle_simplified.sh
```

## Keyboard Shortcuts

Global hotkey support is available on Windows, macOS, and Linux X11. It is automatically disabled on Wayland. Configure the hotkey in Settings → Global Hotkey by focusing the field and pressing your desired combination. A Reset button clears it.

**Note for macOS users:** Global hotkeys are fully supported through the SharpHook library. No additional setup required.

> Shortcuts are implemented with the [SharpHook](https://sharphook.tolik.io/) library. 
> Check its documentation for platform-specific limitations.

On Wayland, use the provided toggle scripts and bind them in your DE (examples below).

### GNOME Example:
```bash
gsettings set org.gnome.settings-daemon.plugins.media-keys custom-keybindings "['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/']"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/ name "Toggle WhisperVoiceInput"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/ command "/path/to/transcribe_toggle_simplified.sh"
gsettings set org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/custom0/ binding "<Ctrl><Alt>w"
```

### KDE Example:
1. System Settings > Shortcuts > Custom Shortcuts
2. Add a new shortcut
3. Set the command to `/path/to/transcribe_toggle_simplified.sh`
4. Assign a keyboard shortcut

## Troubleshooting

Local [Seq server](https://datalust.co/seq) is supported and should be reachable on `http://localhost:5341`.

### Recording Issues

- Ensure your microphone is properly connected and set as the default input device
- Check system permissions for microphone access
- On macOS: grant microphone access in System Settings > Privacy & Security > Microphone

### Transcription Issues

- Verify your API key is correct (if required by your endpoint)
- Check your internet connection
- Ensure the server address is correct
- Try a different Whisper model (smaller models may be faster but less accurate)

### Post‑Processing Issues

- Verify endpoint URL, model, and API key
- If using a local LLM gateway, confirm it’s running and reachable

### Socket Control Issues

- Ensure the application is running
- Check if the socket file exists at `/tmp/WhisperVoiceInput/pipe`
- Verify `socat` is installed: `sudo apt install socat`

## Logs

On Linux/macOS: `~/.config/WhisperVoiceInput/logs/`
On Windows: `%APPDATA%\WhisperVoiceInput\logs\`

## Architecture (actor-based)

Actors and responsibilities:
- MainOrchestratorActor (FSM): Coordinates the pipeline (Idle → Recording → Transcribing → PostProcessing → Saving). Supervises children, freezes settings per session, stashes settings updates, notifies UI via Observer. Fires the optional completion hook before result saving.
- AudioRecordingActor: Records audio via SoundFlow (miniaudio backend) and encodes to MP3 or WAV (based on user setting). Emits AudioRecordedEvent.
- TranscribingActor: Calls `{ServerAddress}/v1/audio/transcriptions` with model/language/prompt (async via PipeTo). Emits TranscriptionCompletedEvent. Handles temp file cleanup/move and deletes temp file on timeout/failure.
- PostProcessorActor (optional): Uses Microsoft.Extensions.AI to enhance text. Emits PostProcessedEvent.
- ResultSaverActor: Outputs final text per selected strategy (clipboard, wl-copy, ydotool, wtype, Wayland IME with configurable fallback, or none). Emits ResultSavedEvent.
- ObserverActor: Bridges actor system to UI with IObservable<StateUpdatedEvent>.
- SocketListenerActor (Linux): Listens on `/tmp/WhisperVoiceInput/pipe` and forwards `transcribe_toggle` to the orchestrator.

Primary messages:
- Commands: ToggleCommand, UpdateSettingsCommand, RecordCommand, StopRecordingCommand, TranscribeCommand(audioPath), PostProcessCommand(text), StartListeningCommand, StopListeningCommand, GetStateObservableCommand
- Events: AudioRecordedEvent, TranscriptionCompletedEvent, PostProcessedEvent, ResultAvailableEvent, ResultSavedEvent, StateUpdatedEvent, StateObservableResult

## Testing

A dedicated test project validates the actor pipeline.

- FSM/Unit tests for `MainOrchestratorActor` transitions and messaging
- Pipeline integration tests using `TestScheduler` for deterministic timing
- Error scenario tests (network timeouts, auth failures, file not found, multi‑error cases)
- Dataset saving behavior with and without post‑processing
- Completion hook pipeline tests (hook enabled/disabled, combined with None output type)
- `ShellHelper` unit tests (shell escaping, placeholder substitution)

Project layout (simplified):
```
WhisperVoiceInput.Tests/
  Actors/
    MainOrchestratorActorTests.cs
    PipelineIntegrationTests.cs
    SpecificErrorScenariosTests.cs
    CompletionHookTests.cs
  Helpers/
    ShellHelperTests.cs
  TestBase/
    AkkaTestBase.cs
  TestDoubles/
    ... (probes, mocks, configurable error actors)
```

## Diagrams

### Data Flow

```mermaid
flowchart LR
    UI["UI / ViewModels"] -- Toggle --> Orchestrator["MainOrchestratorActor (FSM)"]
    SettingsService -- UpdateSettingsCommand --> Orchestrator

    Orchestrator -- RecordCommand --> Audio["AudioRecordingActor"]
    Audio -- AudioRecordedEvent --> Orchestrator
    Audio -- (self) RecordingTimeout --> Audio

    Orchestrator -- TranscribeCommand --> Trans["TranscribingActor"]
    Trans -- TranscriptionCompletedEvent --> Orchestrator
    Trans -- (self) TranscriptionTimeout --> Trans

    Orchestrator -- PostProcessCommand --> Post["PostProcessorActor (optional)"]
    Post -- PostProcessedEvent --> Orchestrator
    Post -- (self) PostProcessingTimeout --> Post

    Orchestrator -. "Completion Hook (fire & forget)" .-> Hook["Shell Process (optional)"]
    Orchestrator -- ResultAvailableEvent --> Saver["ResultSaverActor"]
    Saver -- ResultSavedEvent --> Orchestrator

    Orchestrator -- StateUpdatedEvent --> Observer["ObserverActor"]
    Observer -- StateObservableResult --> UI

    Socket["SocketListenerActor (/tmp/WhisperVoiceInput/pipe)"] -- transcribe_toggle --> Orchestrator
```

### Supervision (runtime)

```mermaid
flowchart TD
    subgraph user["/user/"]
      Orchestrator[MainOrchestratorActor]
      Observer[ObserverActor]
      subgraph SocketSup["SocketSupervisorActor"]
        SocketListener[SocketListenerActor]
      end
    end

    Orchestrator --> Audio[AudioRecordingActor]
    Orchestrator --> Trans[TranscribingActor]
    Orchestrator --> Post[PostProcessorActor]
    Orchestrator --> Saver[ResultSaverActor]

    Note["Note: SocketSupervisorActor exists but current listener is created as top-level sibling under /user."]
```

### FSM States

```mermaid
stateDiagram-v2
    [*] --> idle
    idle --> recording: ToggleCommand
    recording --> transcribing: AudioRecordedEvent
    transcribing --> postprocessing: TranscriptionCompletedEvent
    postprocessing --> saving: PostProcessedEvent
    transcribing --> saving: (post-processing disabled)
    saving --> idle: ResultSavedEvent

    recording --> idle: error after retries or user timeout
    transcribing --> idle: error after retries or user timeout
    postprocessing --> idle: error after retries or user timeout
    saving --> idle: error after retries
```

### Sequence (happy path + error path)

```mermaid
sequenceDiagram
    participant User as User
    participant UI as UI/ViewModel
    participant Orch as MainOrchestrator
    participant Aud as AudioRecording
    participant Tr as Transcribing
    participant PP as PostProcessing
    participant Sav as ResultSaver
    participant Obs as Observer

    User->>UI: Toggle
    UI->>Orch: ToggleCommand
    Orch->>Aud: RecordCommand
    Aud-->>Orch: AudioRecordedEvent
    Orch->>Tr: TranscribeCommand
    alt Success
        Tr-->>Orch: TranscriptionCompletedEvent(text)
        alt Post-processing enabled
            Orch->>PP: PostProcessCommand(text)
            PP-->>Orch: PostProcessedEvent(processed)
            Orch->>Sav: ResultAvailableEvent(processed)
        else Disabled
            Orch->>Sav: ResultAvailableEvent(text)
        end
        Sav-->>Orch: ResultSavedEvent
        Orch-->>Obs: StateUpdatedEvent(Success)
    else Error
        Note over Tr,Orch: Error at any stage (recording/transcribing/post-processing/saving)
        Orch-->>Obs: StateUpdatedEvent(Error, details)
        Orch->>Orch: Cleanup and transition to Idle
    end
    Obs-->>UI: IObservable<StateUpdatedEvent>
```

## License

[MIT License](LICENSE)

## Acknowledgements

- [OpenAI Whisper](https://github.com/openai/whisper) - Speech recognition model
- [Avalonia UI](https://avaloniaui.net/) - Cross-platform UI framework
- [ReactiveUI](https://www.reactiveui.net/) - MVVM framework
- [SoundFlow](https://github.com/LSXPrime/SoundFlow) - Cross-platform audio engine (miniaudio + FFmpeg)
- [Akka.NET](https://getakka.net/) — Actor framework
- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) — AI abstractions for post‑processing
- [SharpHook](https://sharphook.tolik.io/) — Global hotkey support