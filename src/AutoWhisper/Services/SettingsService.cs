using System.IO;
using System.Text.Json;
using SharpHook.Data;

namespace AutoWhisper.Services;

public record WhisperModel(string Name, string FileName, string DisplayName, long SizeMB, string DownloadUrl);

public class AppSettings
{
    public KeyCode HotkeyKey { get; set; } = KeyCode.VcSpace;
    public EventMask HotkeyModifiers { get; set; } = EventMask.LeftCtrl | EventMask.LeftShift;
    public string SelectedModel { get; set; } = "small";
    public string Language { get; set; } = "auto";
    public bool LaunchAtStartup { get; set; } = false;
    public string SelectedMicrophone { get; set; } = "";
    public int SilenceThreshold { get; set; } = 200;
    public bool NormalizeAudio { get; set; } = true;
}

public class SettingsService
{
    private static readonly string AppFolder = AppDomain.CurrentDomain.BaseDirectory;

    private static readonly string SettingsFilePath = Path.Combine(AppFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static readonly WhisperModel[] AvailableModels =
    [
        new("tiny",      "ggml-tiny.bin",      "Tiny (39 MB)",        39, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"),
        new("base",      "ggml-base.bin",      "Base (142 MB)",      142, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"),
        new("small",     "ggml-small.bin",     "Small (466 MB)",     466, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"),
        new("medium",    "ggml-medium.bin",    "Medium (1.5 GB)",   1500, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin"),
        new("large-v3",  "ggml-large-v3.bin",  "Large v3 (3.1 GB)", 3100, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin"),
    ];

    public static readonly (string Code, string Name)[] SupportedLanguages =
    [
        ("auto", "Auto-detect"),
        ("en", "English"),
        ("nl", "Dutch"),
        ("fr", "French"),
        ("de", "German"),
        ("es", "Spanish"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ru", "Russian"),
        ("zh", "Chinese"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("ar", "Arabic"),
        ("hi", "Hindi"),
        ("pl", "Polish"),
        ("tr", "Turkish"),
        ("uk", "Ukrainian"),
        ("sv", "Swedish"),
        ("da", "Danish"),
        ("no", "Norwegian"),
        ("fi", "Finnish"),
        ("cs", "Czech"),
        ("ro", "Romanian"),
        ("hu", "Hungarian"),
        ("el", "Greek"),
        ("he", "Hebrew"),
        ("th", "Thai"),
        ("vi", "Vietnamese"),
        ("id", "Indonesian"),
    ];

    public AppSettings Settings { get; private set; } = new();

    public string ModelsFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");

    public string GetModelPath(WhisperModel model)
    {
        return Path.Combine(ModelsFolder, model.FileName);
    }

    public bool IsModelDownloaded(WhisperModel model)
    {
        return File.Exists(GetModelPath(model));
    }

    public string ResolveModelPath()
    {
        var (path, _, _) = ResolveModelPathWithDiagnostics();
        return path;
    }

    public (string Path, bool IsFallback, string? FallbackModelName) ResolveModelPathWithDiagnostics()
    {
        // Find the selected model
        var selected = Array.Find(AvailableModels, m => m.Name == Settings.SelectedModel);
        if (selected is not null)
        {
            var path = GetModelPath(selected);
            if (File.Exists(path))
                return (path, false, null);
        }

        // Fallback: try any downloaded model (with warning)
        foreach (var model in AvailableModels)
        {
            var path = GetModelPath(model);
            if (File.Exists(path))
                return (path, true, model.Name);
        }

        return ("", false, null);
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Settings = new AppSettings();
                Save();
                return;
            }

            var json = File.ReadAllText(SettingsFilePath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load settings (using defaults): {ex}");
            // Back up the corrupt file for diagnostics
            try
            {
                if (File.Exists(SettingsFilePath))
                    File.Copy(SettingsFilePath, SettingsFilePath + ".corrupt", overwrite: true);
            }
            catch { /* best effort */ }
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save settings: {ex.Message}");
        }
    }
}
