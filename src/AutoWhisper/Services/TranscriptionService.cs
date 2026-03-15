using System.IO;
using Whisper.net;

namespace AutoWhisper.Services;

public class TranscriptionService : IDisposable
{
    private readonly SettingsService _settings;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private bool _isInitialized;
    private bool _disposed;

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

                var (modelPath, isFallback, fallbackModelName) = _settings.ResolveModelPathWithDiagnostics();
                if (string.IsNullOrEmpty(modelPath))
                {
                    LoadError = "No Whisper model found. Please download a model in Settings.";
                    _isInitialized = false;
                    return;
                }

                if (isFallback)
                {
                    Logger.Log($"WARNING: Selected model '{_settings.Settings.SelectedModel}' not found. Using fallback: '{fallbackModelName}'");
                    LoadError = $"Selected model not found; using {fallbackModelName} instead. Download your preferred model in Settings.";
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
                if (!isFallback)
                    LoadError = null;
                Logger.Log($"Whisper model loaded successfully: {Path.GetFileName(modelPath)} (runtime: {RuntimeDetectionService.GetRuntimeDisplayName(runtime)})");
            }
            catch (Exception ex)
            {
                LoadError = $"Failed to load Whisper model: {ex.Message}";
                _isInitialized = false;
                Logger.Log($"Whisper model load failed: {ex}");
            }
        });
    }

    public async Task ReloadAsync()
    {
        Logger.Log("Reloading Whisper model...");
        await InitializeAsync();
        if (_isInitialized)
            Logger.Log("Whisper model reloaded successfully.");
        else
            Logger.Log($"Whisper model reload failed: {LoadError}");
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
        if (_disposed) return;
        _disposed = true;
        _processor?.Dispose();
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
