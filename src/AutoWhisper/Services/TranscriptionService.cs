using System.IO;
using Whisper.net;

namespace AutoWhisper.Services;

public class TranscriptionService
{
    private readonly SettingsService _settings;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public string? LoadError { get; private set; }

    public TranscriptionService(SettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _processor?.Dispose();
                _factory?.Dispose();

                var runtime = RuntimeDetectionService.DetectBestRuntime();
                Logger.Log($"GPU runtime detected: {RuntimeDetectionService.GetRuntimeDisplayName(runtime)}");
                Logger.Log($"Selected model (settings): {_settings.Settings.SelectedModel}");
                Logger.Log($"Custom model path (settings): {(string.IsNullOrEmpty(_settings.Settings.ModelPath) ? "(none)" : _settings.Settings.ModelPath)}");

                var modelPath = _settings.ResolveModelPath();
                if (string.IsNullOrEmpty(modelPath))
                {
                    LoadError = "No Whisper model found. Please configure a model path in Settings.";
                    _isInitialized = false;
                    return;
                }

                Logger.Log($"Resolved model path: {modelPath}");
                _factory = WhisperFactory.FromPath(modelPath);
                var builder = _factory.CreateBuilder();

                var lang = _settings.Settings.Language;
                if (string.IsNullOrEmpty(lang) || lang == "auto")
                    builder.WithLanguageDetection();
                else
                    builder.WithLanguage(lang);

                _processor = builder.Build();

                _isInitialized = true;
                LoadError = null;
                Logger.Log($"Whisper model loaded successfully: {Path.GetFileName(modelPath)} (runtime: {RuntimeDetectionService.GetRuntimeDisplayName(runtime)})");
            }
            catch (Exception ex)
            {
                LoadError = $"Failed to load Whisper model: {ex.Message}";
                _isInitialized = false;
                Logger.Log($"Whisper model load failed: {ex.Message}");
            }
        });
    }

    public async Task ReloadAsync()
    {
        Console.Error.WriteLine("[AutoWhisper] Reloading Whisper model...");
        await InitializeAsync();
        if (_isInitialized)
            Console.Error.WriteLine("[AutoWhisper] Whisper model reloaded successfully.");
        else
            Console.Error.WriteLine($"[AutoWhisper] Whisper model reload failed: {LoadError}");
    }

    public async Task<string> TranscribeAsync(MemoryStream audioStream)
    {
        if (!_isInitialized || _processor is null)
            throw new InvalidOperationException("Transcription service is not initialized.");

        return await Task.Run(async () =>
        {
            var segments = new List<string>();

            await foreach (var segment in _processor.ProcessAsync(audioStream))
            {
                var text = segment.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    segments.Add(text);
            }

            return string.Join(" ", segments);
        });
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
