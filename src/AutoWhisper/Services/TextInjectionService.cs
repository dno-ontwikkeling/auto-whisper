using System.Windows;
using InputSimulatorStandard;
using InputSimulatorStandard.Native;

namespace AutoWhisper.Services;

public class TextInjectionService
{
    private readonly InputSimulator _simulator = new();

    public async Task InjectTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Save previous clipboard and set new text — must run on UI thread
        DataObject? previousClipboard = null;
        bool clipboardSet = false;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                previousClipboard = CloneClipboard();
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard read locked, content will not be restored: {ex.Message}");
            }

            try
            {
                Clipboard.SetText(text);
                clipboardSet = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard write failed, falling back to keyboard typing: {ex.Message}");
                _simulator.Keyboard.TextEntry(text);
            }
        });

        if (!clipboardSet) return;

        // Delay off the UI thread
        await Task.Delay(50);

        // Simulate Ctrl+V (does not require UI thread)
        _simulator.Keyboard.ModifiedKeyStroke(
            VirtualKeyCode.CONTROL,
            VirtualKeyCode.VK_V);

        // Delay then restore clipboard on UI thread
        await Task.Delay(100);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (previousClipboard is not null)
                    Clipboard.SetDataObject(previousClipboard, copy: true);
                else
                    Clipboard.Clear();
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard restore failed: {ex.Message}");
            }
        });
    }

    private static readonly string[] CloneFormats =
    [
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.Rtf,
        DataFormats.Html,
        DataFormats.FileDrop,
        DataFormats.Bitmap,
    ];

    /// <summary>
    /// Deep-copies the current clipboard contents into a new DataObject.
    /// The live COM data object returned by Clipboard.GetDataObject() becomes
    /// invalid once the clipboard is overwritten, so we must snapshot the data.
    /// </summary>
    private static DataObject? CloneClipboard()
    {
        var source = Clipboard.GetDataObject();
        if (source is null) return null;

        var clone = new DataObject();
        bool hasData = false;

        foreach (var format in CloneFormats)
        {
            if (!source.GetDataPresent(format)) continue;
            try
            {
                var data = source.GetData(format);
                if (data is not null)
                {
                    clone.SetData(format, data);
                    hasData = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard clone skipped format '{format}': {ex.Message}");
            }
        }

        return hasData ? clone : null;
    }
}
