using System.IO;

namespace AutoWhisper.Services;

public static class Logger
{
    private static readonly string LogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoWhisper");

    private static readonly string LogFile = Path.Combine(LogFolder, "autowhisper.log");
    private static readonly object Lock = new();

    static Logger()
    {
        Directory.CreateDirectory(LogFolder);
        // Truncate log on startup
        File.WriteAllText(LogFile, $"=== AutoWhisper started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (Lock)
        {
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
    }

    public static string FilePath => LogFile;
}
