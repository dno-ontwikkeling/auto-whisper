# AutoWhisper

A Windows system tray application for voice-to-text dictation. Hold a hotkey, speak, and the transcribed text is automatically pasted at your cursor position — in any application.

All processing happens locally using OpenAI's Whisper model. No cloud services, no API keys, no data leaves your machine.

## Features

- **Hold-to-record dictation** — press and hold a configurable hotkey (default: Ctrl+Shift+Space), speak, release to transcribe and paste
- **Fully offline** — Whisper runs locally, audio is processed in-memory only
- **GPU acceleration** — auto-detects CUDA (NVIDIA) and Vulkan (AMD/Intel) with CPU fallback
- **Multiple models** — 9 Whisper model sizes from Tiny (39 MB) to Large v3 (3.1 GB)
- **29+ languages** — auto-detection or manual language selection
- **System tray** — runs silently in the background with single-instance enforcement
- **Visual overlay** — animated recording indicator with elapsed time

## Getting Started

### Prerequisites

- Windows 10 or 11
- .NET 10 SDK
- Optional: [CUDA Toolkit 12+](https://developer.nvidia.com/cuda-downloads) for NVIDIA GPU acceleration, or Vulkan SDK for AMD/Intel GPU acceleration

### Build and Run

```bash
dotnet build src/AutoWhisper/AutoWhisper.csproj -c Release
dotnet run --project src/AutoWhisper/AutoWhisper.csproj
```

### First Run

1. The settings window opens on first launch
2. Download a Whisper model (start with **Base** or **Small** for a good speed/accuracy balance)
3. Select your language and microphone
4. Configure a hotkey if the default (Ctrl+Shift+Space) doesn't suit you
5. Close settings — the app moves to the system tray

### Installer

An Inno Setup installer script (`installer.iss`) is included. Build with [Inno Setup](https://jrsoftware.org/isinfo.php) to produce a standalone setup executable.

## Usage

1. Hold the hotkey to start recording
2. Speak into your microphone
3. Release the hotkey
4. The overlay shows transcription progress, then the text is pasted at the cursor

Works in any application that accepts text input (editors, browsers, chat apps, etc.).

## Tech Stack

| Component | Library |
|---|---|
| Framework | .NET 10, WPF |
| UI Theme | WPF-UI 4.2 (Fluent Design) |
| Speech-to-Text | Whisper.net 1.9 |
| Audio Capture | NAudio 2.2 |
| Global Hotkeys | SharpHook 7.1 |
| Text Injection | InputSimulatorStandard |
| System Tray | Hardcodet.NotifyIcon.Wpf |

## Project Structure

```
src/AutoWhisper/
├── Program.cs                          # Entry point, single-instance mutex
├── App.xaml.cs                         # Tray icon, service wiring
├── Services/
│   ├── AudioCaptureService.cs          # Microphone recording (16kHz mono)
│   ├── HotkeyService.cs               # Global keyboard hook
│   ├── TranscriptionService.cs         # Whisper.net inference
│   ├── TextInjectionService.cs         # Clipboard-based paste
│   ├── SettingsService.cs              # JSON config persistence
│   ├── RuntimeDetectionService.cs      # GPU detection
│   ├── HotkeyDisplayHelper.cs         # Hotkey formatting for display
│   └── Logger.cs                       # File logging
├── State/
│   └── DictationStateMachine.cs        # Idle → Recording → Transcribing → Pasting
└── Views/
    ├── SettingsWindow.xaml             # Configuration UI
    └── RecordingOverlay.xaml           # Recording indicator
```

## Configuration

Settings are stored in `settings.json` next to the executable. Logs are written to `%APPDATA%\AutoWhisper\autowhisper.log`.
