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

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Preserve current clipboard content
            IDataObject? previousClipboard = null;
            try
            {
                previousClipboard = Clipboard.GetDataObject();
            }
            catch
            {
                // Clipboard may be locked by another app
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // If clipboard fails, fall back to typing
                _simulator.Keyboard.TextEntry(text);
                return;
            }

            // Small delay to ensure clipboard is populated
            Thread.Sleep(50);

            // Simulate Ctrl+V
            _simulator.Keyboard.ModifiedKeyStroke(
                VirtualKeyCode.CONTROL,
                VirtualKeyCode.VK_V);

            // Small delay then restore clipboard
            Thread.Sleep(100);

            try
            {
                if (previousClipboard is not null)
                    Clipboard.SetDataObject(previousClipboard);
            }
            catch
            {
                // Best effort restoration
            }
        });
    }
}
