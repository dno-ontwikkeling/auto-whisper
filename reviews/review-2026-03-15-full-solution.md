# Code Review: AutoWhisper Full Solution
**Date:** 2026-03-15
**Reviewers:** Multi-Agent (security, performance, architecture, simplicity, silent-failure, csharp-idioms)
**Target:** Entire solution (all source files)

## Summary
- **P1 Critical Issues:** 5
- **P2 Important Issues:** 12
- **P3 Nice-to-Have:** 7
- **Confidence Threshold:** 70

---

## P1 - Critical (Must Fix)

- [ ] **#1 [ARCH/FUNC]** GPU runtime detected but never passed to Whisper (Confidence: 97)
  - Location: `Services/TranscriptionService.cs:44`, `Services/RuntimeDetectionService.cs`
  - Issue: `RuntimeDetectionService.DetectBestRuntime()` is called and logged, but the result is never passed to `WhisperFactory.FromPath()`. Whisper.net defaults to CPU unless explicitly told to use CUDA/Vulkan. Users think they have GPU acceleration but are running on CPU. The entire 61-line RuntimeDetectionService is effectively dead code.
  - Fix: Pass the runtime to WhisperFactory, or delete RuntimeDetectionService if GPU selection isn't needed yet.
  - Agents: architecture, simplicity, performance

- [ ] **#2 [CSHARP]** Thread.Sleep blocks UI dispatcher thread for 150ms per paste (Confidence: 97)
  - Location: `Services/TextInjectionService.cs:40,48`
  - Issue: `Thread.Sleep(50)` and `Thread.Sleep(100)` run inside `Dispatcher.InvokeAsync`, freezing the entire WPF UI thread. This stalls the recording overlay animation, tray icon, and all rendering on every paste.
  - Fix: Split into separate dispatcher calls with `await Task.Delay()` between them. Only clipboard operations need the UI thread.
  - Agents: csharp, performance, security

- [ ] **#3 [CSHARP]** TranscriptionService has Dispose() but doesn't implement IDisposable (Confidence: 99)
  - Location: `Services/TranscriptionService.cs:98`
  - Issue: `Dispose()` is a public method but the class doesn't declare `IDisposable`. `using` won't work, static analysis can't warn, and `App.xaml.cs` never calls Dispose on exit — native Whisper handles are leaked.
  - Fix: Implement `IDisposable`. Call `_transcriptionService?.Dispose()` in `App.ExitApplication()` and `OnExit()`.
  - Agent: csharp

- [ ] **#4 [SILENT-FAILURE]** Unhandled UI exceptions swallowed globally with no persistent log (Confidence: 95)
  - Location: `App.xaml.cs:27-31`
  - Issue: All unhandled UI exceptions written to `Console.Error` only (invisible in tray app), marked `Handled = true`. App continues in corrupted state. No `Logger.Log()` call.
  - Fix: Add `Logger.Log()` call and optionally show a MessageBox for unexpected errors.
  - Agent: silent-failure

- [ ] **#5 [SILENT-FAILURE]** async void OnHotkeyUp leaks audioStream and discards exception details (Confidence: 93)
  - Location: `State/DictationStateMachine.cs:88-108`
  - Issue: If `TranscribeAsync` throws, `audioStream.Dispose()` (line 90) is never reached. The `finally` block transitions to Idle without disposing. `Error?.Invoke(ex.Message)` loses the stack trace, and if no subscriber exists, the failure vanishes entirely.
  - Fix: Move `audioStream?.Dispose()` to `finally`. Log full exception with `Logger.Log($"Transcription failed: {ex}")`.
  - Agents: silent-failure, csharp

---

## P2 - Important (Should Fix)

- [ ] **#6 [SILENT-FAILURE]** Logger.Log has no exception handling — disk I/O failure crashes callers (Confidence: 92)
  - Location: `Services/Logger.cs:16-28`
  - Issue: `File.AppendAllText` with no try/catch. Disk-full or locked-file crashes every callsite. Static constructor also unguarded — if `%APPDATA%\AutoWhisper` can't be created, `TypeInitializationException` crashes the app.
  - Fix: Add try/catch with `_available` flag to gracefully degrade. Wrap static constructor body.
  - Agent: silent-failure

- [ ] **#7 [SILENT-FAILURE]** ResolveModelPath silently falls back to wrong model (Confidence: 92)
  - Location: `Services/SettingsService.cs:110-117`
  - Issue: When selected model isn't downloaded, silently falls back to any available model. Confirmed in live logs: user selected "tiny" but got "small". No warning shown.
  - Fix: Return fallback info, log warning, surface via `LoadError`. Or remove fallback entirely — the caller already handles empty string with a clear error message.
  - Agents: silent-failure, simplicity, architecture

- [ ] **#8 [CSHARP]** AudioCaptureService doesn't implement IDisposable (Confidence: 95)
  - Location: `Services/AudioCaptureService.cs`
  - Issue: Holds `WaveInEvent` (OS audio resources), `MemoryStream`, `WaveFileWriter` but no `IDisposable`. If app exits mid-recording, microphone may be locked for other apps.
  - Fix: Implement `IDisposable`, add to `App.ExitApplication()` and `OnExit()`.
  - Agent: csharp

- [ ] **#9 [SECURITY]** Model download with no integrity check (Confidence: 95)
  - Location: `Views/SettingsWindow.xaml.cs:156`
  - Issue: Models downloaded from HuggingFace and loaded into native Whisper.net runtime without hash verification. Compromised CDN could serve malicious binary.
  - Fix: Add SHA-256 checksums to `WhisperModel` record, verify after download before rename.
  - Agent: security

- [ ] **#10 [PERF]** Logger opens/closes file on every log line (Confidence: 95)
  - Location: `Services/Logger.cs:26`
  - Issue: `File.AppendAllText` opens, writes, closes per entry. With 100ms tick timer during recording, that's 10+ file handle operations per second.
  - Fix: Use a persistent `StreamWriter` with per-line flush.
  - Agent: performance

- [ ] **#11 [SILENT-FAILURE]** SetAutoStart silently fails, creating persistent UI lie (Confidence: 90)
  - Location: `Views/SettingsWindow.xaml.cs:320`
  - Issue: If `OpenSubKey` returns null, method returns silently. `LaunchAtStartup = true` already saved to settings. UI shows "on" but registry entry never created.
  - Fix: Log failure, revert setting, update toggle, inform user.
  - Agent: silent-failure

- [ ] **#12 [SILENT-FAILURE]** SettingsService.Load/Save unguarded (Confidence: 85)
  - Location: `Services/SettingsService.cs:121-138`
  - Issue: Corrupt `settings.json` throws `JsonException` propagating to startup. `Save()` silently loses settings on disk-full. No backup/recovery.
  - Fix: Wrap in try/catch, backup corrupt file, fall back to defaults.
  - Agent: silent-failure

- [ ] **#13 [PERF]** Full audio buffer copied to heap array for RMS check (Confidence: 93)
  - Location: `Services/AudioCaptureService.cs:80`
  - Issue: Allocates `byte[]` equal to entire recording minus WAV header (5min = ~9.6MB). Held under lock, blocking `OnDataAvailable`.
  - Fix: Stream RMS calculation using stack-allocated chunk buffer.
  - Agent: performance

- [ ] **#14 [PERF]** RecordingOverlay created/destroyed per recording session (Confidence: 90)
  - Location: `App.xaml.cs:88-89`
  - Issue: New WPF window on every recording start (XAML parsing, storyboard setup, compositor). Adds latency at the moment user expects instant feedback.
  - Fix: Pre-create at startup, hide/show instead of create/close.
  - Agent: performance

- [ ] **#15 [SILENT-FAILURE]** AudioCaptureService recording error not propagated (Confidence: 88)
  - Location: `Services/AudioCaptureService.cs:53`
  - Issue: Hardware errors only logged at `ex.Message` level. Not propagated — state machine transcribes corrupt audio. No Error event on AudioCaptureService.
  - Fix: Store exception, surface in `StopRecording()`.
  - Agent: silent-failure

- [ ] **#16 [SECURITY]** Temp download file not cleaned up on cancel/error (Confidence: 90)
  - Location: `Views/SettingsWindow.xaml.cs:154`
  - Issue: `.tmp` partial download persists on cancellation or error. A 3.1GB model half-downloaded leaves 1.5GB temp file.
  - Fix: Add `File.Delete(tempPath)` in the `finally` block.
  - Agent: security

- [ ] **#17 [CSHARP]** Microphone selection not restored on settings load (Confidence: 78)
  - Location: `Views/SettingsWindow.xaml.cs:67-68`
  - Issue: `LoadSettings()` always sets `MicrophoneCombo.SelectedIndex = 0` regardless of `Settings.SelectedMicrophone`. Saved preference ignored.
  - Fix: Match `SelectedMicrophone` against combo items.
  - Agent: csharp

---

## P3 - Nice-to-Have

- [ ] **#18 [PERF]** RuntimeDetectionService P/Invoke on every call, not cached (Confidence: 92)
  - Location: `Services/RuntimeDetectionService.cs:14`
  - Issue: Loads/frees native DLLs on every call (startup + every settings window + every model reload). GPU hardware doesn't change at runtime.
  - Fix: Use `Lazy<GpuRuntime>` for single detection.
  - Agent: performance

- [ ] **#19 [SILENT-FAILURE]** GPU detection bare catch blocks conceal unexpected failures (Confidence: 87)
  - Location: `Services/RuntimeDetectionService.cs:33,47`
  - Issue: `catch { return false; }` swallows everything. Security exceptions, access violations treated same as missing DLL.
  - Fix: Catch `DllNotFoundException` specifically, log unexpected exceptions.
  - Agent: silent-failure

- [ ] **#20 [SILENT-FAILURE]** TextInjectionService clipboard failures have no logging (Confidence: 85)
  - Location: `Services/TextInjectionService.cs:23,32,55`
  - Issue: Three empty catch blocks. Clipboard failures invisible — users lose content without diagnosis.
  - Fix: Add `Logger.Log()` to each catch.
  - Agent: silent-failure

- [ ] **#21 [SIMPLICITY]** RecordingOverlay.xaml 143 lines of copy-pasted storyboard markup (Confidence: 92)
  - Location: `Views/RecordingOverlay.xaml:25-168`
  - Issue: 12 rectangles with identical animations differing only in `Canvas.Left` and `BeginTime`. 143 lines of duplication.
  - Fix: Generate bars and storyboards in code-behind loop (~25 lines replaces 143).
  - Agent: simplicity

- [ ] **#22 [SIMPLICITY]** HotkeyDisplayHelper F-key cases redundant with default (Confidence: 80)
  - Location: `Services/HotkeyDisplayHelper.cs:54-65`
  - Issue: F1-F12 switch arms produce same result as the `_ => key.ToString().Replace("Vc", "")` fallback.
  - Fix: Remove 12 explicit arms.
  - Agent: simplicity

- [ ] **#23 [CSHARP]** Double dispose in ExitApplication + OnExit (Confidence: 90)
  - Location: `App.xaml.cs:196-208`
  - Issue: `_hotkeyService?.Dispose()` and `_trayIcon?.Dispose()` called in both `ExitApplication()` and `OnExit()`. `Shutdown()` triggers `OnExit`.
  - Fix: Remove dispose calls from `ExitApplication()`, keep only in `OnExit()`.
  - Agent: simplicity, csharp

- [ ] **#24 [SECURITY]** Settings file in executable directory instead of %APPDATA% (Confidence: 82)
  - Location: `Services/SettingsService.cs:23`
  - Issue: Settings written next to exe. Fails under `Program Files` (UAC), exposed on shared machines. Logger already uses `%APPDATA%` correctly.
  - Fix: Move to `%APPDATA%\AutoWhisper\settings.json`.
  - Agent: security, architecture

---

## Cross-Cutting Analysis

### Root Causes Identified

| Root Cause | Findings Affected | Suggested Fix |
|------------|-------------------|---------------|
| GPU runtime detected but never consumed | #1, #18, #19 | Either wire it to WhisperFactory or delete RuntimeDetectionService |
| Logger is fragile infrastructure | #6, #10, #4, #20 | Make Logger resilient (try/catch + StreamWriter), then add Logger.Log to all catch blocks |
| Missing IDisposable implementations | #3, #5, #8, #23 | Implement IDisposable on services, centralize cleanup in OnExit |
| Silent fallbacks without notification | #7, #11, #15 | Surface fallback info to user via UI or log warnings |
| No error handling in settings I/O | #12, #24 | Guard Load/Save, move to %APPDATA% |

### Single-Fix Opportunities

1. **Logger resilience + StreamWriter** — Fixes #6, #10 and unblocks #4, #19, #20 (~20 lines)
2. **IDisposable on services + cleanup in OnExit** — Fixes #3, #5, #8, #23 (~40 lines)
3. **GPU runtime passthrough or deletion** — Fixes #1, #18, #19 (~5-10 lines)
4. **TextInjectionService async refactor** — Fixes #2, #20 (~30 lines)

### Context Files (Read Before Fixing)

| File | Reason | Referenced By |
|------|--------|---------------|
| `Services/Logger.cs` | Must be resilient before other fixes can add logging | all agents |
| `Services/TranscriptionService.cs` | Core init, runtime passthrough, IDisposable | architecture, csharp |
| `State/DictationStateMachine.cs` | Audio stream lifecycle, error handling | silent-failure, csharp |
| `Services/TextInjectionService.cs` | UI thread blocking, clipboard exposure | csharp, performance, security |
| `App.xaml.cs` | Composition root, dispose lifecycle, exception handler | all agents |

---

## Recommended Actions

1. **Immediate:** Fix P1 items — GPU passthrough (#1), UI thread blocking (#2), IDisposable (#3), exception handler (#4), stream leak (#5)
2. **This sprint:** Logger resilience (#6, #10) first, then remaining P2 items
3. **Follow-up:** P3 items as quality improvements

## Next Steps

| Option | When to Use |
|--------|-------------|
| **Work directly** | Start fixing the P1 issues now |
| **Triage** (`/wiz:triage`) | Create stories and prioritize systematically |
