# Implementation Plan: AutoWhisper - Windows Background Dictation Tool

**Created:** 2026-03-14
**Status:** Draft
**Estimated Effort:** L

## Summary

AutoWhisper is a Windows system tray application built with WPF and .NET 9 that enables voice-to-text dictation anywhere on the desktop. The user holds a configurable hotkey to record speech, and on release, the audio is transcribed locally using OpenAI's Whisper model (via Whisper.net) and pasted at the current cursor position. The UI uses WPF-UI (Fluent Design) for a modern Windows 11 look. The app auto-detects GPU availability (CUDA, Vulkan, or CPU fallback) and ships with a bundled `ggml-base.en.bin` model.

## Research Findings

### Is Whisper the Best Choice?

**Yes, for this use case.** Here is the competitive landscape:

| Model | Accuracy | .NET Integration | Windows Native | Status |
|-------|----------|-----------------|---------------|--------|
| **Whisper (via Whisper.net)** | Excellent | Native NuGet | Yes | Active, v1.9.0 |
| NVIDIA NeMo Canary | Best-in-class | REST/Docker only | No | Active but Linux-first |
| Vosk | Moderate | NuGet exists | Yes | Active but lower accuracy |
| Coqui STT | Good | Legacy | Yes | Defunct (2024) |
| DeepSpeech | Moderate | Legacy | Yes | Discontinued (2025) |

**Whisper wins** because it has production-ready .NET bindings (Whisper.net 1.9.0), runs natively on Windows without Docker/Python, supports GPU acceleration via CUDA and Vulkan, and produces good punctuation natively at medium+ model sizes. NeMo Canary has better native punctuation but requires Docker/WSL2, making it impractical for a desktop app.

### Punctuation and Auto-Correction

Whisper natively produces punctuation, capitalization, and sentence spacing. Larger models (`base` and above) are reliable for commas and periods. For v1, Whisper's native output is sufficient. A post-processing LLM step can be added in a future version if needed.

### Technology Stack

| Concern | Choice | NuGet Package | Version |
|---------|--------|---------------|---------|
| UI Framework | WPF | (built-in) | .NET 9 |
| Modern styling | Fluent Design | `WPF-UI` | 4.2.0 |
| System tray | H.NotifyIcon | `H.NotifyIcon.Wpf` | 2.4.1 |
| Global hotkeys | SharpHook (key-down/up) | `SharpHook` | 7.1.1 |
| Microphone capture | NAudio | `NAudio` | 2.2.1 |
| Transcription | Whisper.net | `Whisper.net` | 1.9.0 |
| GPU: NVIDIA | CUDA runtime | `Whisper.net.Runtime.Cuda.Windows` | 1.9.0 |
| GPU: AMD/Intel | Vulkan runtime | `Whisper.net.Runtime.Vulkan` | 1.9.0 |
| GPU: None | CPU runtime | `Whisper.net.Runtime` | 1.9.0 |
| Paste simulation | InputSimulator | `InputSimulatorStandard` | 1.0.2 |
| Single instance | Named Mutex | (BCL) | - |
| Auto-start | Registry HKCU | (BCL) | - |

### Architecture Decisions

- **Hold-to-record**: SharpHook provides key-down/key-up events needed for hold-to-record. NHotkey only provides key-press events.
- **Batch-on-release transcription**: Record while key is held, transcribe full buffer on release. No streaming needed.
- **Audio format**: Record at 16kHz, 16-bit, mono PCM (Whisper's native format).
- **Text injection**: Clipboard paste (Ctrl+V) with clipboard preservation (save user's clipboard, paste, restore).
- **Model bundling**: Ship `ggml-base.en.bin` (142 MB) in the installer. Allow user to download/select other models in settings.
- **GPU auto-detection**: Ship all three runtimes (CPU, CUDA, Vulkan). Detect at startup and use the best available.

## Application State Machine

```
                    ┌──────────┐
                    │   IDLE   │◄──────────────────────┐
                    └────┬─────┘                       │
                         │ hotkey down                 │
                    ┌────▼─────┐                       │
                    │RECORDING │                       │
                    └────┬─────┘                       │
                         │ hotkey up                   │
                    ┌────▼──────┐                      │
                    │TRANSCRIBING│──── error ──────────┤
                    └────┬──────┘                      │
                         │ success                     │
                    ┌────▼─────┐                       │
                    │ PASTING  │───────────────────────┘
                    └──────────┘
```

States:
- **IDLE**: Waiting for hotkey. Tray icon shows default state.
- **RECORDING**: Hotkey held down. Capturing audio. Visual indicator shown (overlay + tray icon change).
- **TRANSCRIBING**: Hotkey released. Processing audio through Whisper. Spinner/progress indicator.
- **PASTING**: Transcription complete. Injecting text at cursor. Returns to IDLE.

## Project Structure

```
AutoWhisper/
├── AutoWhisper.sln
├── src/
│   └── AutoWhisper/
│       ├── AutoWhisper.csproj
│       ├── App.xaml / App.xaml.cs          # App entry, tray icon, single instance
│       ├── Program.cs                      # Main entry point, mutex
│       ├── Models/
│       │   └── ggml-base.en.bin            # Bundled Whisper model (CopyToOutput)
│       ├── Assets/
│       │   ├── tray-idle.ico
│       │   ├── tray-recording.ico
│       │   └── tray-processing.ico
│       ├── Views/
│       │   ├── SettingsWindow.xaml          # Main settings window (FluentWindow)
│       │   ├── RecordingOverlay.xaml        # Small overlay shown during recording
│       │   └── FirstRunDialog.xaml          # First-run setup
│       ├── ViewModels/
│       │   ├── SettingsViewModel.cs
│       │   └── RecordingOverlayViewModel.cs
│       ├── Services/
│       │   ├── HotkeyService.cs            # SharpHook global hotkey management
│       │   ├── AudioCaptureService.cs       # NAudio microphone recording
│       │   ├── TranscriptionService.cs      # Whisper.net inference wrapper
│       │   ├── TextInjectionService.cs      # Clipboard paste with preservation
│       │   ├── RuntimeDetectionService.cs   # GPU/CPU detection and runtime selection
│       │   └── SettingsService.cs           # JSON settings persistence
│       ├── State/
│       │   └── DictationStateMachine.cs     # State transitions: Idle→Recording→Transcribing→Pasting
│       └── Helpers/
│           └── SingleInstanceGuard.cs       # Named mutex wrapper
├── tests/
│   └── AutoWhisper.Tests/
│       ├── AutoWhisper.Tests.csproj
│       └── Services/
│           ├── TranscriptionServiceTests.cs
│           ├── TextInjectionServiceTests.cs
│           └── DictationStateMachineTests.cs
└── plans/
    └── autowhisper-implementation.md        # This file
```

## Implementation Order (TDD)

### Step 1: Project Scaffolding and Build Verification

- **Implement:** Create solution, WPF project with .NET 9, add all NuGet references, verify it builds and launches an empty window.
- **Validation:** `dotnet build` succeeds, app launches and closes cleanly.

### Step 2: Single Instance Guard and System Tray

- **Test:** Verify mutex prevents second instance. Verify tray icon appears.
- **Implement:** `Program.cs` with mutex, `App.xaml.cs` with `H.NotifyIcon` tray icon, context menu (Settings / Exit).
- **Validation:** App shows in system tray, second launch shows message, right-click menu works.

### Step 3: Settings Service and Persistence

- **Test:** Settings save/load round-trip to JSON file in `%APPDATA%\AutoWhisper\`.
- **Implement:** `SettingsService.cs` with default hotkey, model path, auto-start toggle, and selected runtime.
- **Validation:** Settings file created on first run with defaults, persisted across restarts.

### Step 4: Global Hotkey Service (SharpHook)

- **Test:** Key-down and key-up events fire correctly, configurable key combination.
- **Implement:** `HotkeyService.cs` wrapping SharpHook `TaskPoolGlobalHook`, exposing `HotkeyDown` and `HotkeyUp` events.
- **Validation:** Console output confirms key-down/key-up detection with configured hotkey.

### Step 5: Audio Capture Service

- **Test:** Recording produces valid 16kHz/16-bit/mono WAV data. Start/stop works cleanly.
- **Implement:** `AudioCaptureService.cs` using NAudio `WaveInEvent`, accumulating to `MemoryStream`.
- **Validation:** Record 3 seconds, verify WAV header and sample rate.

### Step 6: Transcription Service (Whisper.net)

- **Test:** Given a WAV buffer, returns transcribed text string. Handles empty audio gracefully.
- **Implement:** `TranscriptionService.cs` loading model from bundled path or AppData, `ProcessAsync` on background thread.
- **Implement:** `RuntimeDetectionService.cs` to detect CUDA/Vulkan/CPU and select appropriate runtime.
- **Validation:** Transcribe a test WAV file, verify output text.
- **Depends on:** Step 1 (NuGet packages available)

### Step 7: Text Injection Service

- **Test:** Text is pasted at cursor position. Clipboard is preserved (original content restored after paste).
- **Implement:** `TextInjectionService.cs` using `Clipboard.SetText` + `InputSimulator` Ctrl+V, with clipboard save/restore.
- **Validation:** Open Notepad, run injection, verify text appears and original clipboard content is restored.

### Step 8: Dictation State Machine

- **Test:** State transitions: Idle→Recording→Transcribing→Pasting→Idle. Error transitions return to Idle.
- **Implement:** `DictationStateMachine.cs` orchestrating HotkeyService → AudioCaptureService → TranscriptionService → TextInjectionService.
- **Depends on:** Steps 4, 5, 6, 7
- **Validation:** Full flow: hold key → speak → release → text appears in Notepad.

### Step 9: Recording Overlay UI

- **Implement:** `RecordingOverlay.xaml` - small, always-on-top, transparent-background window with a red recording indicator and elapsed time. Appears near system tray or bottom-center of screen.
- **Depends on:** Step 8
- **Validation:** Overlay appears during recording, disappears on release. Shows elapsed time.

### Step 10: Settings Window (Fluent UI)

- **Implement:** `SettingsWindow.xaml` using WPF-UI `FluentWindow` with:
  - General: Hotkey configuration, launch-at-startup toggle
  - Model: Model file path, model download options, active runtime display
  - Audio: Microphone device selection
- **Depends on:** Steps 3, 4
- **Validation:** All settings persist and take effect without restart where possible.

### Step 11: First-Run Experience

- **Implement:** `FirstRunDialog.xaml` shown on first launch: microphone permission check, hotkey display, model status verification.
- **Depends on:** Steps 3, 10
- **Validation:** First launch shows dialog, subsequent launches go straight to tray.

### Step 12: Auto-Start with Windows

- **Implement:** Registry `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` integration, toggled from Settings.
- **Depends on:** Step 10
- **Validation:** Toggle setting, verify registry entry created/removed, verify app starts on login.

### Final: Polish and Edge Cases

- [ ] Discard recordings shorter than 500ms (accidental press)
- [ ] Handle microphone disconnect during recording
- [ ] Handle model file missing/corrupted at startup
- [ ] Tray icon state changes (idle/recording/processing icons)
- [ ] Tray tooltip shows current state
- [ ] Graceful shutdown (stop recording if active, unregister hotkey)
- [ ] Error notifications via tray balloon tips
- **Validation:** Lint clean, all tests pass, manual smoke test of full flow.

## Acceptance Criteria

- [ ] App runs in system tray with no visible window
- [ ] Hold Ctrl+Shift+Space (default) to record, release to transcribe and paste
- [ ] Transcription happens locally via Whisper model with no network calls
- [ ] Text appears at cursor position in any application
- [ ] Punctuation (periods, commas, capitalization) present in output
- [ ] Recording overlay provides visual feedback
- [ ] Settings window allows hotkey, model, microphone, and auto-start configuration
- [ ] GPU auto-detected and used when available (CUDA > Vulkan > CPU)
- [ ] Single instance enforced
- [ ] App can auto-start with Windows
- [ ] Clipboard content preserved across paste operations

## Security Considerations

- Audio is processed in-memory only; never written to disk as temp files.
- No network calls made by the app (fully offline operation).
- No telemetry or crash reporting in v1.
- Model files are read-only; app never modifies them.
- Settings stored in `%APPDATA%` (user-scoped, not system-wide).

## Performance Considerations

- **Model loading**: Whisper model load takes 1-3 seconds. Load once at startup, keep in memory.
- **Transcription latency**: `base.en` on CPU: ~1-2s for 5s audio. On GPU: <500ms. Acceptable for dictation.
- **Memory usage**: ~300-500 MB with model loaded (base.en). Acceptable for a background app.
- **Audio buffer**: In-memory only. 60 seconds of 16kHz/16-bit/mono = ~1.9 MB. Negligible.
- **Hold-to-record maximum**: Soft limit at 60 seconds to prevent unbounded memory growth.

## NuGet Package Reference

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <!-- Modern Fluent UI -->
    <PackageReference Include="WPF-UI" Version="4.2.0" />

    <!-- System tray icon -->
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.4.1" />

    <!-- Global hotkeys (key-down/key-up support) -->
    <PackageReference Include="SharpHook" Version="7.1.1" />

    <!-- Audio recording -->
    <PackageReference Include="NAudio" Version="2.2.1" />

    <!-- Keyboard simulation for paste -->
    <PackageReference Include="InputSimulatorStandard" Version="1.0.2" />

    <!-- Whisper speech-to-text -->
    <PackageReference Include="Whisper.net" Version="1.9.0" />
    <PackageReference Include="Whisper.net.Runtime" Version="1.9.0" />
    <PackageReference Include="Whisper.net.Runtime.Cuda.Windows" Version="1.9.0" />
    <PackageReference Include="Whisper.net.Runtime.Vulkan" Version="1.9.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Models\ggml-base.en.bin">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

## Related Files

All files are new (greenfield project). See Project Structure section above for complete file listing.

---

## Next Steps

When ready to implement, run:
- `/wiz:work plans/autowhisper-implementation.md` - Execute the plan step by step
- `/wiz:deepen-plan` - Get more detail on specific sections
- `/wiz:brainstorming` - Discuss plan details
