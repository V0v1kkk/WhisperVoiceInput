# WhisperVoiceInput

Avalonia desktop app (.NET 9) that records audio, transcribes it via an OpenAI-compatible Whisper API, and outputs text to clipboard/typing/IME. Backend is an Akka.NET actor pipeline. Runs on Linux, macOS, and Windows.

## Build / Run / Test

```bash
dotnet build
dotnet build -c Release
dotnet run --project WhisperVoiceInput/WhisperVoiceInput.csproj
dotnet test
dotnet test -c Release
```

## Project Structure

- **Solution and projects**
  - [WhisperVoiceInput.sln](WhisperVoiceInput.sln)
  - App: [WhisperVoiceInput/WhisperVoiceInput.csproj](WhisperVoiceInput/WhisperVoiceInput.csproj)
  - Tests: [WhisperVoiceInput.Tests/WhisperVoiceInput.Tests.csproj](WhisperVoiceInput.Tests/WhisperVoiceInput.Tests.csproj)

- **Runtime entry points**
  - [WhisperVoiceInput/Program.cs](WhisperVoiceInput/Program.cs)
  - Avalonia app: [WhisperVoiceInput/App.axaml](WhisperVoiceInput/App.axaml), [WhisperVoiceInput/App.axaml.cs](WhisperVoiceInput/App.axaml.cs)

- **Actor-based backend (Akka.NET)**
  - Orchestrator FSM: [MainOrchestratorActor.cs](WhisperVoiceInput/Actors/MainOrchestratorActor.cs)
  - Pipeline actors: [AudioRecordingActor.cs](WhisperVoiceInput/Actors/AudioRecordingActor.cs), [TranscribingActor.cs](WhisperVoiceInput/Actors/TranscribingActor.cs), [PostProcessorActor.cs](WhisperVoiceInput/Actors/PostProcessorActor.cs), [ResultSaverActor.cs](WhisperVoiceInput/Actors/ResultSaverActor.cs)
  - Observer/UI bridge: [ObserverActor.cs](WhisperVoiceInput/Actors/ObserverActor.cs)
  - Socket control: [SocketListenerActor.cs](WhisperVoiceInput/Actors/SocketListenerActor.cs), [SocketSupervisorActor.cs](WhisperVoiceInput/Actors/SocketSupervisorActor.cs)
  - Messages: [Commands.cs](WhisperVoiceInput/Messages/Commands.cs) (`ToggleCommand`, `ReprocessCommand`, `CancelPipelineCommand`, etc.), [Events.cs](WhisperVoiceInput/Messages/Events.cs) (`ReprocessAvailableEvent`, etc.)

- **Helpers**
  - Shell utilities (escape, hook command building): [ShellHelper.cs](WhisperVoiceInput/Helpers/ShellHelper.cs)

- **Services and abstractions**
  - Actor system bootstrap: [ActorSystemManager.cs](WhisperVoiceInput/Services/ActorSystemManager.cs), [ActorPropsFactory.cs](WhisperVoiceInput/Services/ActorPropsFactory.cs)
  - Settings/Clipboard: [SettingsService.cs](WhisperVoiceInput/Services/SettingsService.cs), [ClipboardService.cs](WhisperVoiceInput/Services/ClipboardService.cs)
  - Interfaces: [Abstractions/](WhisperVoiceInput/Abstractions/), [IPipelineController.cs](WhisperVoiceInput/Abstractions/IPipelineController.cs)

- **UI (MVVM)**
  - Views: [Views/](WhisperVoiceInput/Views/MainWindow.axaml), ViewModels: [ViewModels/](WhisperVoiceInput/ViewModels/MainWindowViewModel.cs), Locator: [ViewLocator.cs](WhisperVoiceInput/ViewLocator.cs)

- **Configuration and models**
  - App settings: [AppSettings.cs](WhisperVoiceInput/Models/AppSettings.cs), Retry policy: [RetryPolicySettings.cs](WhisperVoiceInput/Models/RetryPolicySettings.cs)

- **Documentation and scripts**
  - Overview: [readme.md](readme.md)
  - CLI: [transcribe_toggle_simplified.sh](transcribe_toggle_simplified.sh), [transcribe_toggle.sh](transcribe_toggle.sh)

## Actor Pipeline and Message Flow

- **Coordinator (FSM)**: [MainOrchestratorActor.cs](WhisperVoiceInput/Actors/MainOrchestratorActor.cs)
  - States: Idle → Recording → Transcribing → PostProcessing → Saving → Idle
  - `CancelPipelineCommand` returns to Idle from any non-Idle state (stops child actors, optionally retains audio file)
  - `ReprocessCommand` from Idle: re-enters Transcribing with the retained audio file (skips Recording)
  - Freezes `AppSettings` per session; stashes updates while busy
  - Uses `_sessionId` for unique child actor names per session and stale event filtering (events carry optional `SessionId`)
  - Notifies UI via [ObserverActor.cs](WhisperVoiceInput/Actors/ObserverActor.cs) (state updates + reprocess availability)
  - Fires optional completion hook (fire‑and‑forget shell command via [ShellHelper.cs](WhisperVoiceInput/Helpers/ShellHelper.cs)) before result saving

- **Pipeline actors and responsibilities**
  - Recording: [AudioRecordingActor.cs](WhisperVoiceInput/Actors/AudioRecordingActor.cs) → emits `AudioRecordedEvent`
  - Transcription: [TranscribingActor.cs](WhisperVoiceInput/Actors/TranscribingActor.cs)
    - Calls `{ServerAddress}/v1/audio/transcriptions` with model/language/prompt
    - Sends `TranscriptionCompletedEvent` and handles temp file cleanup/move
  - Post‑processing (optional): [PostProcessorActor.cs](WhisperVoiceInput/Actors/PostProcessorActor.cs) → emits `PostProcessedEvent`
  - Result output: [ResultSaverActor.cs](WhisperVoiceInput/Actors/ResultSaverActor.cs) → emits `ResultSavedEvent` (supports `None` output type to skip output)

- **Messages**: [Commands.cs](WhisperVoiceInput/Messages/Commands.cs), [Events.cs](WhisperVoiceInput/Messages/Events.cs)
  - Commands: `ToggleCommand`, `RecordCommand`, `TranscribeCommand`, `PostProcessCommand`, `ReprocessCommand`, `CancelPipelineCommand`, `StartListeningCommand`, `StopListeningCommand`, `GetStateObservableCommand`, `GetReprocessObservableCommand`
  - Events: `AudioRecordedEvent`, `RecordingStartedEvent`, `TranscriptionCompletedEvent(Text, SessionId)`, `PostProcessedEvent(ProcessedText, SessionId)`, `ResultAvailableEvent`, `ResultSavedEvent(Text, SessionId)`, `StateUpdatedEvent`, `ReprocessAvailableEvent(IsAvailable)`, `StateObservableResult`, `ReprocessObservableResult`

- **Supervision and retries**
  - Each actor applies retry policy from [RetryPolicySettings.cs](WhisperVoiceInput/Models/RetryPolicySettings.cs) (see orchestrator usage)
  - Errors at any stage propagate to orchestrator, which publishes a detailed `StateUpdatedEvent` with step name

- **File retention** (`KeepLastRecording` setting)
  - When enabled, `AudioRecordingActor` and `TranscribingActor` skip temp file deletion on error/timeout
  - Orchestrator retains audio file path for reprocessing; cleans up on exit or when setting is toggled off

- **Socket control (Linux)**
  - [SocketListenerActor.cs](WhisperVoiceInput/Actors/SocketListenerActor.cs) listens on `/tmp/WhisperVoiceInput/pipe` and forwards `transcribe_toggle`, `transcribe_cancel`, `transcribe_reprocess`

## Error Handling Policy

- **Source of truth**: Orchestrator publishes errors as `StateUpdatedEvent` with `AppState.Error` and a human‑readable message indicating the failing step.
  - Step names: `MainOrchestratorActor.StepNames` (see usages in tests)
  - Observer/UI bridge: [ObserverActor.cs](WhisperVoiceInput/Actors/ObserverActor.cs)

- **Per‑actor guidance**
  - Transcription: [TranscribingActor.cs](WhisperVoiceInput/Actors/TranscribingActor.cs)
    - Use `EnsureSuccessStatusCode()` for HTTP; parse JSON into a `Text` field
    - Clean up or move audio files after successful transcription; do not fail the pipeline on cleanup issues
  - Result saving runs asynchronously; failures are reported but should not revert the main FSM once results are produced

- **Tests verifying behavior**
  - See [ErrorHandlingTests.cs](WhisperVoiceInput.Tests/Actors/ErrorHandlingTests.cs) for detailed expectations (per‑step messages, sequential errors, timeout‑like cases)

## Coding Conventions

- Use **file-scoped namespaces** for new code (`namespace WhisperVoiceInput.Actors;`)
- Private fields: `_camelCase`; public properties/methods: `PascalCase`
- Actor child names: kebab-case + session id (`"audio-recording-{sessionId}"`)
- Messages: `record` types implementing `ICommand` or `IEvent`, grouped with `#region` in [Commands.cs](WhisperVoiceInput/Messages/Commands.cs) / [Events.cs](WhisperVoiceInput/Messages/Events.cs)
- Async in actors: use `PipeTo(Self, ...)` — never `await` on the actor thread
- Throw from actor handler for supervision retry; `Self.Tell(originalCommand)` before throw if retry is needed
- Manual DI — no container; match composition in [App.axaml.cs](WhisperVoiceInput/App.axaml.cs)
- Logging: Serilog `ILogger` via `.ForContext<T>()`, structured placeholders (`{FilePath}`, not `$"{filePath}"`)
- Non-throwing helpers: `Try*` prefix (`TryDeleteFile`, `TryRunCompletionHook`)
- Nullable enabled; `null!` only where justified (e.g. TestKit stash, design-time ctor)
- New settings: add to `AppSettings` record → expose via `SettingsService` → add `{Name}Input` property + two-way sync in `MainWindowViewModel`
- New UI commands: `ReactiveCommand` on a ViewModel, call an abstraction (`IPipelineController`, `IRecordingToggler`)
- `IActorPropsFactory` is the main test seam — production uses `ActorPropsFactory`, tests use `MockActorPropsFactory` / `ConfigurableErrorPropsFactory`

## Testing Guide

- **Frameworks**: NUnit + Akka.TestKit; base class: [AkkaTestBase.cs](WhisperVoiceInput.Tests/TestBase/AkkaTestBase.cs)
- **Assertions**: FluentAssertions (`.Should().Be(...)`)
- **Test naming**: `Should_...` pattern (`Should_Cancel_During_Recording_And_Return_To_Idle`)

- **Common patterns**
  - Instantiate orchestrator with `ActorOfAsTestFSMRef<...>` and `CallingThreadDispatcher`
  - Use `TestScheduler` for deterministic timing; advance with `TestScheduler.Advance(...)`
  - Observe UI updates via `TestProbe` attached to [ObserverActor.cs](WhisperVoiceInput/Actors/ObserverActor.cs)

- **Fixtures and doubles**
  - Actor props factories and fakes: [TestDoubles/](WhisperVoiceInput.Tests/TestDoubles/)
  - Error injection: [ConfigurableErrorPropsFactory.cs](WhisperVoiceInput.Tests/TestDoubles/ConfigurableErrorPropsFactory.cs)
  - Clipboard mock: [MockClipboardService.cs](WhisperVoiceInput.Tests/TestDoubles/MockClipboardService.cs)

- **Representative suites**
  - Orchestrator FSM: [MainOrchestratorActorTests.cs](WhisperVoiceInput.Tests/Actors/MainOrchestratorActorTests.cs)
  - Pipeline integration: [PipelineIntegrationTests.cs](WhisperVoiceInput.Tests/Actors/PipelineIntegrationTests.cs)
  - Detailed errors: [ErrorHandlingTests.cs](WhisperVoiceInput.Tests/Actors/ErrorHandlingTests.cs)
  - Specific scenarios: [SpecificErrorScenariosTests.cs](WhisperVoiceInput.Tests/Actors/SpecificErrorScenariosTests.cs)
  - Completion hook: [CompletionHookTests.cs](WhisperVoiceInput.Tests/Actors/CompletionHookTests.cs)
  - Shell helper: [ShellHelperTests.cs](WhisperVoiceInput.Tests/Helpers/ShellHelperTests.cs)
