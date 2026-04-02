using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SharpHook.Data;
using AutoWhisper.Services;

namespace AutoWhisper.Views;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly HttpClient HttpClient = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });

    private readonly SettingsService _settingsService;
    private readonly HotkeyService _hotkeyService;
    private bool _isCapturingHotkey;
    private bool _isLoading = true;
    private CancellationTokenSource? _downloadCts;
    private AudioCaptureService? _previewService;
    private DispatcherTimer? _levelTimer;
    private double _smoothedRms;

    public event Action? SettingsSaved;

    public SettingsWindow(SettingsService settingsService, HotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;

        var settings = _settingsService.Settings;

        HotkeyDisplay.Text = HotkeyDisplayHelper.FormatHotkey(settings.HotkeyModifiers, settings.HotkeyKey);
        StartupToggle.IsChecked = settings.LaunchAtStartup;

        // Populate model combo
        PopulateModelCombo();

        // Populate language combo
        LanguageCombo.Items.Clear();
        int langIndex = 0;
        for (int i = 0; i < SettingsService.SupportedLanguages.Length; i++)
        {
            var (code, name) = SettingsService.SupportedLanguages[i];
            LanguageCombo.Items.Add(name);
            if (code == settings.Language)
                langIndex = i;
        }
        LanguageCombo.SelectedIndex = langIndex;

        var runtime = RuntimeDetectionService.DetectBestRuntime();
        RuntimeDisplay.Text = RuntimeDetectionService.GetRuntimeDisplayName(runtime);

        PopulateMicrophoneCombo(settings.SelectedMicrophone);

        ThresholdSlider.Value = settings.SilenceThreshold;
        ThresholdValueText.Text = $"Threshold: {settings.SilenceThreshold}";
        NormalizeToggle.IsChecked = settings.NormalizeAudio;
        UpdateThresholdMarker();

        _isLoading = false;
    }

    private void AutoSave()
    {
        if (_isLoading) return;

        var settings = _settingsService.Settings;
        settings.LaunchAtStartup = StartupToggle.IsChecked == true;
        settings.SelectedMicrophone = MicrophoneCombo.SelectedItem?.ToString() ?? "";
        settings.SilenceThreshold = (int)ThresholdSlider.Value;
        settings.NormalizeAudio = NormalizeToggle.IsChecked == true;

        var selectedModel = GetSelectedModel();
        if (selectedModel is not null)
            settings.SelectedModel = selectedModel.Name;

        var langIndex = LanguageCombo.SelectedIndex;
        if (langIndex >= 0 && langIndex < SettingsService.SupportedLanguages.Length)
            settings.Language = SettingsService.SupportedLanguages[langIndex].Code;

        _settingsService.Save();
        SetAutoStart(settings.LaunchAtStartup);
        SettingsSaved?.Invoke();
    }

    private void PopulateModelCombo()
    {
        ModelCombo.Items.Clear();
        int selectedIndex = 0;

        for (int i = 0; i < SettingsService.AvailableModels.Length; i++)
        {
            var model = SettingsService.AvailableModels[i];
            var downloaded = _settingsService.IsModelDownloaded(model);
            var label = downloaded ? $"{model.DisplayName}  \u2713" : model.DisplayName;
            ModelCombo.Items.Add(label);

            if (model.Name == _settingsService.Settings.SelectedModel)
                selectedIndex = i;
        }

        ModelCombo.SelectedIndex = selectedIndex;
        UpdateDownloadButton();
    }

    private WhisperModel? GetSelectedModel()
    {
        var index = ModelCombo.SelectedIndex;
        if (index < 0 || index >= SettingsService.AvailableModels.Length)
            return null;
        return SettingsService.AvailableModels[index];
    }

    private void UpdateDownloadButton()
    {
        var model = GetSelectedModel();
        if (model is null) return;

        var downloaded = _settingsService.IsModelDownloaded(model);
        DownloadModelButton.Content = downloaded ? "Downloaded" : "Download";
        DownloadModelButton.IsEnabled = !downloaded;
    }

    private void ModelCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateDownloadButton();
        AutoSave();
    }

    private async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        var model = GetSelectedModel();
        if (model is null) return;

        _downloadCts = new CancellationTokenSource();
        DownloadModelButton.IsEnabled = false;
        DownloadModelButton.Content = "Downloading...";
        DownloadProgressBorder.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        DownloadProgressText.Text = $"Downloading {model.DisplayName}...";

        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_settingsService.ModelsFolder);
            var destPath = _settingsService.GetModelPath(model);
            tempPath = destPath + ".tmp";

            using (var response = await HttpClient.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using (var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token))
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                {
                    var buffer = new byte[81920];
                    long bytesRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, _downloadCts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), _downloadCts.Token);
                        bytesRead += read;

                        if (totalBytes > 0)
                        {
                            var pct = (double)bytesRead / totalBytes * 100;
                            DownloadProgressBar.Value = pct;
                            DownloadProgressText.Text = $"Downloading {model.DisplayName}... {bytesRead / (1024 * 1024)} / {totalBytes / (1024 * 1024)} MB";
                        }
                    }
                }
            }

            // Streams are closed — safe to rename
            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tempPath, destPath);
            tempPath = null; // rename succeeded, don't delete in finally

            DownloadProgressText.Text = $"{model.DisplayName} downloaded successfully.";
            PopulateModelCombo();
        }
        catch (OperationCanceledException)
        {
            DownloadProgressText.Text = "Download cancelled.";
        }
        catch (Exception ex)
        {
            DownloadProgressText.Text = $"Download failed: {ex.Message}";
            DownloadModelButton.IsEnabled = true;
            DownloadModelButton.Content = "Download";
        }
        finally
        {
            _downloadCts = null;
            // Clean up partial download file
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); }
                catch { /* best effort */ }
            }
        }
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturingHotkey)
        {
            StopHotkeyCapture();
            return;
        }

        _isCapturingHotkey = true;

        ChangeHotkeyButton.Content = "Cancel";
        HotkeyDisplay.Text = "Press a key combo...";
        HotkeyBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xEF, 0x44, 0x44));

        // Move focus away from the Cancel button so key presses don't activate it
        HotkeyBorder.Focusable = true;
        HotkeyBorder.Focus();

        _hotkeyService.HotkeyCaptured += OnHotkeyCaptured;
        _hotkeyService.StartCapture();
    }

    private void OnHotkeyCaptured(EventMask modifiers, KeyCode key)
    {
        // Escape cancels capture
        if (key == KeyCode.VcEscape)
        {
            Dispatcher.Invoke(StopHotkeyCapture);
            return;
        }

        // Require at least one modifier
        if (modifiers == EventMask.None)
        {
            Dispatcher.Invoke(() =>
            {
                HotkeyDisplay.Text = "Need a modifier (Ctrl/Shift/Alt)";
            });
            return;
        }

        Dispatcher.Invoke(() =>
        {
            _settingsService.Settings.HotkeyKey = key;
            _settingsService.Settings.HotkeyModifiers = modifiers;
            HotkeyDisplay.Text = HotkeyDisplayHelper.FormatHotkey(modifiers, key);
            StopHotkeyCapture();
            AutoSave();
        });
    }

    private void StopHotkeyCapture()
    {
        _isCapturingHotkey = false;
        ChangeHotkeyButton.Content = "Change";
        HotkeyBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));

        if (HotkeyDisplay.Text == "Press a key combo...")
            HotkeyDisplay.Text = HotkeyDisplayHelper.FormatHotkey(
                _settingsService.Settings.HotkeyModifiers,
                _settingsService.Settings.HotkeyKey);

        _hotkeyService.HotkeyCaptured -= OnHotkeyCaptured;
        _hotkeyService.StopCapture();
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        AutoSave();
    }

    private void LanguageCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        AutoSave();
    }

    private void PopulateMicrophoneCombo(string selectedMicrophone)
    {
        MicrophoneCombo.Items.Clear();
        var devices = AudioCaptureService.GetAvailableDevices();
        int micIndex = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            MicrophoneCombo.Items.Add(devices[i]);
            if (devices[i] == selectedMicrophone)
                micIndex = i;
        }

        if (MicrophoneCombo.Items.Count > 0)
            MicrophoneCombo.SelectedIndex = micIndex;
    }

    private void RefreshMicrophones_Click(object sender, RoutedEventArgs e)
    {
        var current = MicrophoneCombo.SelectedItem?.ToString() ?? "";
        _isLoading = true;
        PopulateMicrophoneCombo(current);
        _isLoading = false;
        AutoSave();
    }

    private void MicrophoneCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        AutoSave();
    }

    private void TestMic_Click(object sender, RoutedEventArgs e)
    {
        if (_previewService is { IsPreviewing: true })
        {
            StopMicPreview();
            return;
        }

        var devices = AudioCaptureService.GetAvailableDevices();
        int deviceNumber = 0;
        var selected = MicrophoneCombo.SelectedItem?.ToString() ?? "";
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i] == selected)
            {
                deviceNumber = i;
                break;
            }
        }

        _previewService = new AudioCaptureService();
        _previewService.StartPreview(deviceNumber);
        _smoothedRms = 0;

        _levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _levelTimer.Tick += LevelTimer_Tick;
        _levelTimer.Start();

        TestMicButton.Content = "Stop";
    }

    private void StopMicPreview()
    {
        _levelTimer?.Stop();
        _levelTimer = null;
        _previewService?.StopPreview();
        _previewService?.Dispose();
        _previewService = null;
        _smoothedRms = 0;
        MicLevelBar.Value = 0;
        RmsValueText.Text = "RMS: \u2014";
        TestMicButton.Content = "Test Mic";
    }

    private void LevelTimer_Tick(object? sender, EventArgs e)
    {
        if (_previewService is null) return;

        double raw = _previewService.LatestRms;
        _smoothedRms = _smoothedRms * 0.7 + raw * 0.3;
        MicLevelBar.Value = _smoothedRms;
        RmsValueText.Text = $"RMS: {_smoothedRms:F0}";
        UpdateThresholdMarker();
    }

    private void UpdateThresholdMarker()
    {
        double max = ThresholdSlider.Maximum;
        if (max <= 0) return;
        double ratio = ThresholdSlider.Value / max;
        double availableWidth = MicLevelBar.ActualWidth;
        if (availableWidth <= 0) return;
        Canvas.SetLeft(ThresholdMarker, ratio * availableWidth);
    }

    private void MicLevelBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThresholdMarker();
    }

    private void ThresholdSlider_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        e.Handled = true; // prevent scroll from changing slider value
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ThresholdValueText.Text = $"Threshold: {(int)ThresholdSlider.Value}";
        UpdateThresholdMarker();
        AutoSave();
    }

    private void NormalizeToggle_Changed(object sender, RoutedEventArgs e)
    {
        AutoSave();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        StopHotkeyCapture();
        StopMicPreview();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopMicPreview();
        if (_isCapturingHotkey)
            StopHotkeyCapture();
        _downloadCts?.Cancel();
        base.OnClosed(e);
    }

    private void SetAutoStart(bool enable)
    {
        const string registryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "AutoWhisper";

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryKey, writable: true);
            if (key is null)
            {
                Logger.Log($"SetAutoStart failed: registry key '{registryKey}' could not be opened.");
                if (enable)
                {
                    _settingsService.Settings.LaunchAtStartup = false;
                    _settingsService.Save();
                    _isLoading = true;
                    StartupToggle.IsChecked = false;
                    _isLoading = false;
                }
                return;
            }

            if (enable)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath is not null)
                    key.SetValue(appName, $"\"{exePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(appName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"SetAutoStart failed: {ex.Message}");
        }
    }
}
