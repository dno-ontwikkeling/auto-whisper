using System.Windows;

namespace AutoWhisper;

public static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main()
    {
        const string mutexName = "AutoWhisper_SingleInstance_7A3F2B1E";
        _mutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show(
                "AutoWhisper is already running in the system tray.",
                "AutoWhisper",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        finally
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
