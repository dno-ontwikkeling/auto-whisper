# Implementation Plan: Quiet Microphone Handling

**Created:** 2026-04-02
**Status:** Draft
**Estimated Effort:** M

## Summary

Add three interconnected features to handle quiet microphones:

1. **Configurable silence threshold** — Replace the hardcoded RMS 100 with a user-configurable value persisted in settings.
2. **Live level meter with threshold slider** — In the Audio section of Settings, show a real-time microphone level bar with an overlaid draggable threshold marker. The user speaks, sees where the bar lands, and drags the threshold just below it.
3. **Audio normalization** — After recording passes the silence gate, normalize the audio to a consistent RMS level (~-14 dBFS) before sending to Whisper, so quiet mics produce the same transcription quality as loud ones.

## Research Findings

### Repository Patterns
- Settings UI uses a consistent card pattern: `Border Background="#10FFFFFF" CornerRadius="8" Padding="16"` with a `DockPanel` inside.
- All setting changes call `AutoSave()` immediately — no explicit save button.
- `_isLoading` flag guards event handlers during initialization.
- `AudioCaptureService` records 16kHz/16-bit/mono with 50ms buffers (~20 callbacks/sec).
- The existing RMS calculation in `StopRecording()` uses raw short values (0–32767 scale), not normalized floats.
- `DictationStateMachine` already holds `_settingsService` and can pass threshold to `StopRecording()`.
- No tests exist in the project.

### Best Practices (NAudio + WPF)
- `WaveInEvent.DataAvailable` fires on a background thread — never touch UI directly from it.
- Best metering pattern: compute RMS in `DataAvailable`, store via `Volatile.Write`, read from a `DispatcherTimer` on the UI thread (~20fps).
- Apply exponential smoothing (decay ~0.85) for a visually stable meter.
- For normalization, RMS-based targeting -14 dBFS (linear ~0.20) is better than peak normalization for speech recognition. Cap gain at 6x to avoid amplifying noise.
- `MemoryStream.GetBuffer()` allows in-place sample modification without allocations.

### Silence Threshold Reference Values (16-bit, 16kHz mono)
| Condition | RMS (raw) |
|---|---|
| Near-silence (idle mic) | 10–50 |
| Room background noise | 50–300 |
| Soft/whispered speech | 300–800 |
| Normal speech | 800–4000 |
| Loud speech | 4000–15000 |

Current hardcoded threshold: 100. Recommended new default: 200.

## Implementation Order

### Step 1: Add settings properties

- **File:** `src/AutoWhisper/Services/SettingsService.cs`
- **Change:** Add to `AppSettings`:
  ```csharp
  public int SilenceThreshold { get; set; } = 200;
  public bool NormalizeAudio { get; set; } = true;
  ```
- **Validation:** Project builds, existing settings.json loads without error (new properties get defaults).

### Step 2: Make silence threshold configurable in AudioCaptureService

- **File:** `src/AutoWhisper/Services/AudioCaptureService.cs`
- **Change:** Add `int silenceThreshold` parameter to `StopRecording()`. Replace hardcoded `100` on line 111 with the parameter.
- **File:** `src/AutoWhisper/State/DictationStateMachine.cs`
- **Change:** Pass `_settingsService.Settings.SilenceThreshold` to `StopRecording()` at line 80.
- **Validation:** Build succeeds. Logging still shows "Audio below silence threshold" when appropriate.

### Step 3: Add audio normalization to StopRecording

- **File:** `src/AutoWhisper/Services/AudioCaptureService.cs`
- **Change:** After the silence threshold check passes (line 117), if normalization is requested, normalize the PCM data in-place:
  1. The existing RMS pass already computed `rms` and `sampleCount` — reuse these values.
  2. Compute `gain = targetRms / rms`, clamped to max 6.0.
  3. Iterate samples via `_audioBuffer.GetBuffer()`, multiply each by gain, clamp to `short.MinValue`/`short.MaxValue`.
  4. Log the gain applied.
- Add `bool normalize` parameter to `StopRecording()` alongside `silenceThreshold`.
- **File:** `src/AutoWhisper/State/DictationStateMachine.cs`
- **Change:** Pass `_settingsService.Settings.NormalizeAudio` to `StopRecording()`.
- **Validation:** Build succeeds. Log shows normalization gain when applied.

### Step 4: Add live level metering capability to AudioCaptureService

- **File:** `src/AutoWhisper/Services/AudioCaptureService.cs`
- **Change:**
  - Add `private volatile float _latestRms;` field and public `float LatestRms => _latestRms;` property.
  - In `OnDataAvailable`, after writing to the buffer, compute per-buffer RMS from `e.Buffer`/`e.BytesRecorded` and store via `Volatile.Write(ref _latestRms, rms)`.
  - Note: During level preview in Settings, we need a lightweight recording that computes RMS but doesn't buffer audio. Add `StartPreview(int deviceNumber)` and `StopPreview()` methods that create a `WaveInEvent` that only computes RMS (no MemoryStream/WaveFileWriter). This avoids interfering with the main recording pipeline.
- **Validation:** Build succeeds. `LatestRms` updates when preview is active.

### Step 5: Settings UI — Level meter with threshold slider

- **File:** `src/AutoWhisper/Views/SettingsWindow.xaml`
- **Change:** In the Audio section (after the Microphone row), add a new card:
  ```
  ┌─────────────────────────────────────────────────────┐
  │ Silence threshold                          RMS: 200 │
  │ ██████████████████░░░░░░░░░░░░|░░░░░░░░░░░░░░░░░░░ │
  │ [Test Mic]                              [Normalize ✓]│
  └─────────────────────────────────────────────────────┘
  ```
  - A `Grid` overlaying a `ProgressBar` (green level fill) with a re-templated `Slider` (thin white vertical thumb = threshold marker).
  - ProgressBar range 0–5000 (raw RMS). Slider same range, bound to threshold value.
  - A "Test Mic" button to start/stop preview. A "Normalize" checkbox.
  - Display current RMS value as text.

- **File:** `src/AutoWhisper/Views/SettingsWindow.xaml.cs`
- **Change:**
  - Add a dedicated `AudioCaptureService` instance for preview (separate from the one used for dictation).
  - `TestMic_Click`: Toggle preview on/off. When on, call `StartPreview(deviceNumber)` and start a `DispatcherTimer` (50ms interval) that reads `LatestRms` and updates the ProgressBar with exponential smoothing.
  - `ThresholdSlider_ValueChanged`: Update settings + `AutoSave()`.
  - `NormalizeToggle_Changed`: Update settings + `AutoSave()`.
  - Stop preview and dispose timer in `OnClosed`.
  - The Slider template: hide the track, make the thumb a 3px wide white vertical bar.

- **Validation:** Open settings, click Test Mic, speak, see bar move. Drag threshold slider. Close settings — preview stops cleanly.

### Step 6: Cleanup and polish

- Update the log message in `StopRecording()` to reference the configurable threshold value instead of the static comment.
- Ensure preview stops if the user starts an actual recording (hotkey pressed while settings open).
- **Validation:** Full build succeeds. Manual test: record with a quiet mic, verify normalization kicks in and transcription works.

## Acceptance Criteria

- [ ] Silence threshold is configurable via a slider in Settings
- [ ] Live microphone level meter shows real-time audio levels when "Test Mic" is clicked
- [ ] Threshold slider is overlaid on the level meter bar for intuitive calibration
- [ ] Audio normalization can be toggled on/off in Settings
- [ ] Quiet microphone recordings are normalized before Whisper processing
- [ ] Settings persist across app restarts
- [ ] Preview recording stops cleanly when Settings window closes
- [ ] Existing recording/transcription flow works unchanged

## Performance Considerations

- Per-buffer RMS computation adds ~microseconds to each 50ms callback — negligible.
- Normalization pass iterates samples once in-place via `GetBuffer()` — no extra allocation.
- Gain capped at 6x to avoid amplifying background noise on near-silent recordings.
- DispatcherTimer at 50ms is lightweight and stops when preview ends.

## Related Files

- `src/AutoWhisper/Services/SettingsService.cs` — AppSettings model
- `src/AutoWhisper/Services/AudioCaptureService.cs` — Recording, silence gate, normalization
- `src/AutoWhisper/State/DictationStateMachine.cs` — Passes settings to StopRecording
- `src/AutoWhisper/Views/SettingsWindow.xaml` — UI layout
- `src/AutoWhisper/Views/SettingsWindow.xaml.cs` — UI logic, preview, timer

---

## Next Steps

When ready to implement, run:
- `/wiz:work plans/quiet-microphone-handling.md` — Execute the plan
