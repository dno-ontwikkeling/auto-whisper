using SharpHook;
using SharpHook.Data;

namespace AutoWhisper.Services;

public class HotkeyService : IDisposable
{
    private readonly SettingsService _settings;
    private TaskPoolGlobalHook? _hook;
    private bool _isKeyDown;
    private bool _isCapturing;

    public event Action? HotkeyDown;
    public event Action? HotkeyUp;

    /// <summary>
    /// Fired during capture mode when a non-modifier key is pressed.
    /// Provides the normalized modifiers and key code.
    /// </summary>
    public event Action<EventMask, KeyCode>? HotkeyCaptured;

    public HotkeyService(SettingsService settings)
    {
        _settings = settings;
    }


    /// <summary>
    /// Enter capture mode — normal hotkey detection is paused, and the next
    /// non-modifier key press fires HotkeyCaptured instead.
    /// </summary>
    public void StartCapture()
    {
        _isCapturing = true;
        _isKeyDown = false;
    }

    /// <summary>
    /// Exit capture mode — resume normal hotkey detection.
    /// </summary>
    public void StopCapture()
    {
        _isCapturing = false;
        _isKeyDown = false;
    }

    public async Task StartAsync()
    {
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;

        _ = Task.Run(async () => await _hook.RunAsync());

        await Task.Delay(100);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (HotkeyDisplayHelper.IsModifierKey(e.Data.KeyCode)) return;

        if (_isCapturing)
        {
            var normalizedMask = NormalizeModifiers(e.RawEvent.Mask);
            HotkeyCaptured?.Invoke(normalizedMask, e.Data.KeyCode);
            return;
        }

        if (_isKeyDown) return;

        var mask = NormalizeModifiers(e.RawEvent.Mask);
        var requiredKey = _settings.Settings.HotkeyKey;
        var requiredModifiers = _settings.Settings.HotkeyModifiers;

        if (e.Data.KeyCode == requiredKey && ModifiersMatch(mask, requiredModifiers))
        {
            Logger.Log($"Hotkey MATCH: {HotkeyDisplayHelper.FormatHotkey(mask, e.Data.KeyCode)}");
            _isKeyDown = true;
            HotkeyDown?.Invoke();
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (_isCapturing) return;
        if (!_isKeyDown) return;

        if (e.Data.KeyCode == _settings.Settings.HotkeyKey)
        {
            Logger.Log("Hotkey released");
            _isKeyDown = false;
            HotkeyUp?.Invoke();
        }
    }

    private static bool ModifiersMatch(EventMask actual, EventMask required)
    {
        return (actual & required) == required;
    }

    private static EventMask NormalizeModifiers(EventMask mask)
    {
        var result = EventMask.None;
        if (mask.HasFlag(EventMask.LeftCtrl) || mask.HasFlag(EventMask.RightCtrl))
            result |= EventMask.LeftCtrl;
        if (mask.HasFlag(EventMask.LeftShift) || mask.HasFlag(EventMask.RightShift))
            result |= EventMask.LeftShift;
        if (mask.HasFlag(EventMask.LeftAlt) || mask.HasFlag(EventMask.RightAlt))
            result |= EventMask.LeftAlt;
        if (mask.HasFlag(EventMask.LeftMeta) || mask.HasFlag(EventMask.RightMeta))
            result |= EventMask.LeftMeta;
        return result;
    }

    public void Dispose()
    {
        _hook?.Dispose();
    }
}
