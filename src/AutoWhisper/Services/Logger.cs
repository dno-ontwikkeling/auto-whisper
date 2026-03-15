using System.IO;

namespace AutoWhisper.Services;

public static class Logger
{
    private static readonly string LogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoWhisper");

    private static readonly string LogFile = Path.Combine(LogFolder, "autowhisper.log");
    private static readonly object s_lock = new();
    private static StreamWriter? s_writer;
    private static bool s_available = true;

    static Logger()
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            s_writer = new StreamWriter(
                new FileStream(LogFile, FileMode.Create, FileAccess.Write, FileShare.Read, 4096))
            {
                AutoFlush = true
            };
            s_writer.WriteLine($"=== AutoWhisper started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z ===");
        }
        catch
        {
            s_available = false;
        }
    }

    public static void Log(string message)
    {
        if (!s_available) return;

        var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}Z] {message}";
        lock (s_lock)
        {
            try
            {
                s_writer?.WriteLine(line);
            }
            catch
            {
                s_available = false;
            }
        }
    }

    public static string FilePath => LogFile;
}
