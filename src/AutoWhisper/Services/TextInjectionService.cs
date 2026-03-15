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
        IDataObject? previousClipboard = null;
        bool clipboardSet = false;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                previousClipboard = Clipboard.GetDataObject();
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
                    Clipboard.SetDataObject(previousClipboard);
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard restore failed: {ex.Message}");
            }
        });
    }
}
