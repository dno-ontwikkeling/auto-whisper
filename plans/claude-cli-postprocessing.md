# Claude CLI Post-Processing

## Goal
Add a second global hotkey, "dictate + Claude". It records and transcribes as today, then pipes the transcript through the local `claude -p` CLI with a user-selected preset prompt (structured summary, cleanup, custom) and pastes the result at the cursor. The normal hotkey is unchanged. On any Claude failure the raw transcript is pasted and an error is shown, so dictation is never lost. Uses the user's existing Claude CLI login; no API key is stored.

## Success criteria
- [ ] Claude hotkey records, transcribes, runs the active preset and pastes the result at the cursor.
- [ ] Normal hotkey behavior and latency unchanged; main and Claude hotkeys never both fire (modifier-subset overlap handled).
- [ ] Overlay shows a distinct Processing state during the Claude call.
- [ ] `claude` missing from PATH: settings shows "not found"; Claude hotkey pastes raw transcript and shows an error.
- [ ] Timeout (30s), non-zero exit or empty output: raw transcript pasted, error shown, process killed on timeout.
- [ ] CLI run uses no tools and no session persistence; transcript is passed on stdin, never interpolated into a shell command line.
- [ ] Foreground window changed during the wait: result goes to clipboard only, no paste.
- [ ] Presets (name + prompt) editable in settings, persisted in `settings.json`; defaults "Structured summary" and "Clean up" shipped.
- [ ] Settings section follows `DESIGN.md` and states that dictated text is sent to Anthropic.
- [ ] Unit tests cover settings round-trip, hotkey resolution, and `ClaudeCliService` (args, timeout, exit codes, empty output, missing CLI) with a faked process runner.

## Out of scope
- Anthropic API key mode, streaming output, tool use / agent behavior
- Chaining presets, per-preset hotkeys, preview/confirm window, runtime preset picker
- Model selection setting (CLI default is used)

## Steps
- [ ] Step 1: Create xUnit test project referencing the app, add to solution, add one passing sanity test for `TextCorrectionService.Apply` — files: tests/AutoWhisper.Tests/AutoWhisper.Tests.csproj, tests/AutoWhisper.Tests/TextCorrectionServiceTests.cs, AutoWhisper.sln — verify: `dotnet test tests/AutoWhisper.Tests`
- [ ] Step 2: Spike the CLI contract on this machine and record exact working flags (stdin input, `--tools ""`, `--no-session-persistence`, `--system-prompt`, exit codes, executable name `claude.exe` vs `claude.cmd`) in the Log — files: plans/claude-cli-postprocessing.md — verify: `echo "hello world" | claude -p --no-session-persistence --tools "" --system-prompt "Reply with the input in uppercase only"` prints `HELLO WORLD`
- [ ] Step 3: Write failing settings tests: `ClaudePresets` defaults contain "Structured summary" and "Clean up", `ActiveClaudePreset` defaults to first, Claude hotkey defaults differ from main hotkey, JSON round-trip keeps presets and hotkey — files: tests/AutoWhisper.Tests/ClaudeSettingsTests.cs — verify: `dotnet test tests/AutoWhisper.Tests` fails (RED) on missing members
- [ ] Step 4: Add `ClaudePreset` and new `AppSettings` members (`ClaudeHotkeyKey`, `ClaudeHotkeyModifiers`, `ClaudePresets`, `ActiveClaudePreset`) — files: src/AutoWhisper/Services/SettingsService.cs — verify: `dotnet test tests/AutoWhisper.Tests`
- [ ] Step 5: Write failing `ClaudeCliService` tests with a fake `IProcessRunner`: builds args per Step 2 findings with transcript on stdin; success returns trimmed stdout; non-zero exit, empty output, timeout and CLI-not-found each return a failure result carrying the error message — files: tests/AutoWhisper.Tests/ClaudeCliServiceTests.cs — verify: `dotnet test tests/AutoWhisper.Tests` fails (RED)
- [ ] Step 6: Implement `IProcessRunner` + real runner (argument list, redirected stdin/stdout, timeout with kill of process tree, no window), `ClaudeCliLocator` (PATH lookup), and `ClaudeCliService.ProcessAsync(text, preset)` returning a result with `Text`/`Error` — files: src/AutoWhisper/Services/ClaudeCliService.cs, src/AutoWhisper/Services/ProcessRunner.cs — verify: `dotnet test tests/AutoWhisper.Tests`
- [ ] Step 7: Write failing `HotkeyMatcher.Resolve(mask, key, bindings)` tests: exact match wins, Ctrl+Shift+Alt+Space picks Claude not main, Ctrl+Shift+Space picks main, unmatched key returns none, bindings with the same combo flagged as conflict — files: tests/AutoWhisper.Tests/HotkeyMatcherTests.cs — verify: `dotnet test tests/AutoWhisper.Tests` fails (RED)
- [ ] Step 8: Implement `HotkeyMatcher` (most-specific-modifiers wins) and extend `HotkeyService` with `ClaudeHotkeyDown`/`ClaudeHotkeyUp` using it, tracking which binding is held so key-up fires for the right one — files: src/AutoWhisper/Services/HotkeyMatcher.cs, src/AutoWhisper/Services/HotkeyService.cs — verify: `dotnet test tests/AutoWhisper.Tests` and `dotnet build AutoWhisper.sln`
- [ ] Step 9: Add `TextInjectionService.CopyToClipboardAsync` and a `ForegroundWindow` helper (Win32 `GetForegroundWindow`) — files: src/AutoWhisper/Services/TextInjectionService.cs, src/AutoWhisper/Services/ForegroundWindow.cs — verify: `dotnet build AutoWhisper.sln`
- [ ] Step 10: Extend `DictationStateMachine`: `Processing` state, record mode on hotkey down, after corrections run the Claude step in Claude mode, capture foreground window before the call, fallback to raw transcript on failure with `Error` event, clipboard-only if window changed — files: src/AutoWhisper/State/DictationStateMachine.cs — verify: `dotnet build AutoWhisper.sln`
- [ ] Step 11: Overlay Processing state (replace timer with "Claude" label, keep bars) and wire it in `App` (`OnStateChanged`, construct `ClaudeCliService`, tray balloon on `Error`) — files: src/AutoWhisper/Views/RecordingOverlay.xaml, src/AutoWhisper/Views/RecordingOverlay.xaml.cs, src/AutoWhisper/App.xaml.cs — verify: `dotnet build AutoWhisper.sln`
- [ ] Step 12: Settings UI section "Claude" per `DESIGN.md`: CLI status, Claude hotkey capture (reject same combo as main), preset list editor (add/edit/delete, same pattern as word corrections), active preset dropdown, privacy note; autosave like other sections — files: src/AutoWhisper/Views/SettingsWindow.xaml, src/AutoWhisper/Views/SettingsWindow.xaml.cs — verify: `dotnet build AutoWhisper.sln`
- [ ] Step 13: Document the feature (hotkey, presets, CLI requirement, privacy) — files: README.md — verify: `grep -n "Claude" README.md`

## Risks
- Hotkey overlap: `HotkeyService.cs:98` matches modifiers as a subset, so Ctrl+Shift+Alt+Space also satisfies Ctrl+Shift+Space → most-specific-wins resolver (Steps 7-8) plus conflict rejection in settings (Step 12).
- Windows spawn: `claude` may be `claude.cmd` needing a shell → Step 2 spike decides; never build a shell command string with transcript content, always stdin.
- Prompt injection: dictated text may read like instructions → preset prompt passed as system prompt, tools disabled, output only pasted as text.
- Latency 2-5s+ cold start → Processing overlay, 30s timeout, raw-transcript fallback.
- Wrong paste target after wait → foreground window guard (Step 10), clipboard only.
- Privacy: transcript leaves the machine → explicit note in settings and README.
- Clipboard restore race in `TextInjectionService.cs:55-68` after paste → reuse existing flow, do not alter it.
- No test project exists today → Step 1 introduces one; app project is `WinExe` net10.0-windows, so the test project must target the same.

## Open questions
- Default Claude hotkey: assumed Ctrl+Shift+Alt+Space (works with the resolver). Change if it conflicts with the user's setup.
- Model: assumed CLI default, no setting. A fast model (Haiku-class) may suit short dictation; revisit after the Step 2 spike shows latency.

## Log
<!-- appended by /workflow:work: one entry per completed step -->
