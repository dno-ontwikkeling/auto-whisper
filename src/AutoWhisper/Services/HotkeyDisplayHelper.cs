using SharpHook.Data;

namespace AutoWhisper.Services;

public static class HotkeyDisplayHelper
{
    public static string FormatHotkey(EventMask modifiers, KeyCode key)
    {
        var parts = new List<string>();

        bool hasCtrl = modifiers.HasFlag(EventMask.LeftCtrl) || modifiers.HasFlag(EventMask.RightCtrl);
        bool hasAlt = modifiers.HasFlag(EventMask.LeftAlt) || modifiers.HasFlag(EventMask.RightAlt);

        // AltGr shows as Ctrl+Alt on Windows — display it as "AltGr" instead
        if (hasCtrl && hasAlt)
        {
            parts.Add("AltGr");
        }
        else
        {
            if (hasCtrl)
                parts.Add("Ctrl");
            if (hasAlt)
                parts.Add("Alt");
        }

        if (modifiers.HasFlag(EventMask.LeftShift) || modifiers.HasFlag(EventMask.RightShift))
            parts.Add("Shift");
        if (modifiers.HasFlag(EventMask.LeftMeta) || modifiers.HasFlag(EventMask.RightMeta))
            parts.Add("Win");

        parts.Add(FormatKeyCode(key));

        return string.Join(" + ", parts);
    }

    private static string FormatKeyCode(KeyCode key) => key switch
    {
        KeyCode.VcSpace => "Space",
        KeyCode.VcBackspace => "Backspace",
        KeyCode.VcEnter => "Enter",
        KeyCode.VcTab => "Tab",
        KeyCode.VcEscape => "Escape",
        KeyCode.VcDelete => "Delete",
        KeyCode.VcInsert => "Insert",
        KeyCode.VcHome => "Home",
        KeyCode.VcEnd => "End",
        KeyCode.VcPageUp => "PageUp",
        KeyCode.VcPageDown => "PageDown",
        KeyCode.VcUp => "Up",
        KeyCode.VcDown => "Down",
        KeyCode.VcLeft => "Left",
        KeyCode.VcRight => "Right",
        KeyCode.VcF1 => "F1",
        KeyCode.VcF2 => "F2",
        KeyCode.VcF3 => "F3",
        KeyCode.VcF4 => "F4",
        KeyCode.VcF5 => "F5",
        KeyCode.VcF6 => "F6",
        KeyCode.VcF7 => "F7",
        KeyCode.VcF8 => "F8",
        KeyCode.VcF9 => "F9",
        KeyCode.VcF10 => "F10",
        KeyCode.VcF11 => "F11",
        KeyCode.VcF12 => "F12",
        KeyCode.VcCapsLock => "CapsLock",
        KeyCode.VcPrintScreen => "PrintScreen",
        KeyCode.VcScrollLock => "ScrollLock",
        KeyCode.VcPause => "Pause",
        KeyCode.VcBackQuote => "`",
        KeyCode.VcMinus => "-",
        KeyCode.VcEquals => "=",
        KeyCode.VcOpenBracket => "[",
        KeyCode.VcCloseBracket => "]",
        KeyCode.VcBackslash => "\\",
        KeyCode.VcSemicolon => ";",
        KeyCode.VcQuote => "'",
        KeyCode.VcComma => ",",
        KeyCode.VcPeriod => ".",
        KeyCode.VcSlash => "/",
        _ => key.ToString().Replace("Vc", "")
    };

    public static bool IsModifierKey(KeyCode key) => key is
        KeyCode.VcLeftControl or KeyCode.VcRightControl or
        KeyCode.VcLeftShift or KeyCode.VcRightShift or
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta;

    public static EventMask KeyCodeToModifierMask(KeyCode key) => key switch
    {
        KeyCode.VcLeftControl => EventMask.LeftCtrl,
        KeyCode.VcRightControl => EventMask.RightCtrl,
        KeyCode.VcLeftShift => EventMask.LeftShift,
        KeyCode.VcRightShift => EventMask.RightShift,
        KeyCode.VcLeftAlt => EventMask.LeftAlt,
        KeyCode.VcRightAlt => EventMask.RightAlt,
        KeyCode.VcLeftMeta => EventMask.LeftMeta,
        KeyCode.VcRightMeta => EventMask.RightMeta,
        _ => EventMask.None
    };
}
