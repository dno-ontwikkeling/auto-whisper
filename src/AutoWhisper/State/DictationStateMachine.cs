using System.Diagnostics;
using System.Windows;
using AutoWhisper.Services;

namespace AutoWhisper.State;

public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    Pasting
}

public class DictationStateMachine
{
    private readonly HotkeyService _hotkeyService;
    private readonly AudioCaptureService _audioCaptureService;
    private readonly TranscriptionService _transcriptionService;
    private readonly TextInjectionService _textInjectionService;
    private readonly Stopwatch _recordingTimer = new();

    private const int MinRecordingMs = 500;

    public DictationState CurrentState { get; private set; } = DictationState.Idle;

    public event Action<DictationState>? StateChanged;
    public event Action<string>? Error;
    public event Action<TimeSpan>? RecordingTick;

    private System.Threading.Timer? _tickTimer;

    public DictationStateMachine(
        HotkeyService hotkeyService,
        AudioCaptureService audioCaptureService,
        TranscriptionService transcriptionService,
        TextInjectionService textInjectionService)
    {
        _hotkeyService = hotkeyService;
        _audioCaptureService = audioCaptureService;
        _transcriptionService = transcriptionService;
        _textInjectionService = textInjectionService;

        _hotkeyService.HotkeyDown += OnHotkeyDown;
        _hotkeyService.HotkeyUp += OnHotkeyUp;
    }

    private void OnHotkeyDown()
    {
        if (CurrentState != DictationState.Idle) return;

        if (!_transcriptionService.IsInitialized)
        {
            Error?.Invoke(_transcriptionService.LoadError ?? "Whisper model not loaded.");
            return;
        }

        TransitionTo(DictationState.Recording);
        _audioCaptureService.StartRecording();
        _recordingTimer.Restart();

        _tickTimer = new System.Threading.Timer(_ =>
        {
            RecordingTick?.Invoke(_recordingTimer.Elapsed);
        }, null, 0, 100);
    }

    private async void OnHotkeyUp()
    {
        if (CurrentState != DictationState.Recording) return;

        _tickTimer?.Dispose();
        _tickTimer = null;
        _recordingTimer.Stop();

        var audioStream = _audioCaptureService.StopRecording();

        if (_recordingTimer.ElapsedMilliseconds < MinRecordingMs || audioStream is null)
        {
            audioStream?.Dispose();
            TransitionTo(DictationState.Idle);
            return;
        }

        TransitionTo(DictationState.Transcribing);

        try
        {
            var text = await _transcriptionService.TranscribeAsync(audioStream);

            if (string.IsNullOrWhiteSpace(text))
            {
                TransitionTo(DictationState.Idle);
                return;
            }

            TransitionTo(DictationState.Pasting);
            await _textInjectionService.InjectTextAsync(text);
        }
        catch (Exception ex)
        {
            Logger.Log($"Transcription failed: {ex}");
            Error?.Invoke($"Transcription failed: {ex.Message}");
        }
        finally
        {
            audioStream.Dispose();
            TransitionTo(DictationState.Idle);
        }
    }

    private void TransitionTo(DictationState newState)
    {
        CurrentState = newState;
        Application.Current?.Dispatcher.BeginInvoke(() => StateChanged?.Invoke(newState));
    }
}
