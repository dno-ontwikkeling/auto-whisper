using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using AutoWhisper.Services;
using AutoWhisper.State;
using AutoWhisper.Views;

namespace AutoWhisper;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private SettingsService? _settingsService;
    private HotkeyService? _hotkeyService;
    private AudioCaptureService? _audioCaptureService;
    private TranscriptionService? _transcriptionService;
    private TextInjectionService? _textInjectionService;
    private DictationStateMachine? _stateMachine;
    private RecordingOverlay? _recordingOverlay;
    private SettingsWindow? _settingsWindow;
    private Action<TimeSpan>? _tickHandler;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log($"Unhandled UI exception: {args.Exception}");
            Console.Error.WriteLine($"[AutoWhisper] Unhandled UI exception: {args.Exception}");
            args.Handled = true;
        };

        try
        {
            Console.Error.WriteLine("[AutoWhisper] Starting up...");

            _settingsService = new SettingsService();
            _settingsService.Load();
            Console.Error.WriteLine("[AutoWhisper] Settings loaded.");

            _hotkeyService = new HotkeyService(_settingsService);
            _audioCaptureService = new AudioCaptureService();
            _transcriptionService = new TranscriptionService(_settingsService);
            _textInjectionService = new TextInjectionService();

            _stateMachine = new DictationStateMachine(
                _hotkeyService,
                _audioCaptureService,
                _transcriptionService,
                _textInjectionService);

            _stateMachine.StateChanged += OnStateChanged;
            _stateMachine.Error += msg =>
            {
                Logger.Log($"State machine error: {msg}");
                Console.Error.WriteLine($"[AutoWhisper] Error: {msg}");
            };

            CreateTrayIcon();
            Console.Error.WriteLine("[AutoWhisper] Tray icon created.");

            // Pre-create the recording overlay for reuse
            _recordingOverlay = new RecordingOverlay();

            var hotkeyDisplay = HotkeyDisplayHelper.FormatHotkey(
                _settingsService.Settings.HotkeyModifiers,
                _settingsService.Settings.HotkeyKey);

            await _hotkeyService.StartAsync();
            Logger.Log($"Hotkey configured: {hotkeyDisplay}");
            Console.Error.WriteLine($"[AutoWhisper] Hotkey service started. Hold {hotkeyDisplay} to record.");

            await _transcriptionService.InitializeAsync();
            if (_transcriptionService.IsInitialized)
                Console.Error.WriteLine("[AutoWhisper] Whisper model loaded. Ready!");
            else
                Console.Error.WriteLine($"[AutoWhisper] Model load failed: {_transcriptionService.LoadError}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Startup error: {ex}");
            Console.Error.WriteLine($"[AutoWhisper] Startup error: {ex}");
        }
    }

    private void OnStateChanged(DictationState state)
    {
        Console.Error.WriteLine($"[AutoWhisper] State: {state}");

        switch (state)
        {
            case DictationState.Recording:
                // Unsubscribe previous tick handler if any
                if (_tickHandler is not null)
                    _stateMachine!.RecordingTick -= _tickHandler;

                _recordingOverlay?.ResetAndShow();

                _tickHandler = elapsed => _recordingOverlay?.UpdateTimer(elapsed);
                _stateMachine!.RecordingTick += _tickHandler;
                break;

            case DictationState.Transcribing:
            case DictationState.Idle:
                HideOverlay();
                break;
        }
    }

    private void HideOverlay()
    {
        if (_tickHandler is not null)
        {
            _stateMachine!.RecordingTick -= _tickHandler;
            _tickHandler = null;
        }

        _recordingOverlay?.Hide();
    }

    private void CreateTrayIcon()
    {
        var iconUri = new Uri("pack://application:,,,/Assets/app-icon.ico", UriKind.Absolute);
        System.Drawing.Icon? icon = null;
        try
        {
            var iconStream = GetResourceStream(iconUri)?.Stream;
            if (iconStream is not null)
                icon = new System.Drawing.Icon(iconStream);
        }
        catch (Exception ex)
        {
            Logger.Log($"Tray icon load failed: {ex.Message}");
        }

        _trayIcon = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "AutoWhisper - Ready",
            ContextMenu = CreateContextMenu()
        };
        _trayIcon.TrayLeftMouseDown += (_, _) => ShowSettings();
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
            _settingsWindow.Focus();
            return;
        }

        var previousModel = _settingsService!.Settings.SelectedModel;
        var previousLanguage = _settingsService.Settings.Language;
        var previousModelPath = _settingsService.Settings.ModelPath;

        _settingsWindow = new SettingsWindow(_settingsService, _hotkeyService!);
        _settingsWindow.SettingsSaved += async () =>
        {
            var display = HotkeyDisplayHelper.FormatHotkey(
                _settingsService.Settings.HotkeyModifiers,
                _settingsService.Settings.HotkeyKey);
            Logger.Log($"Hotkey changed to: {display}");
            Console.Error.WriteLine($"[AutoWhisper] Hotkey changed to: {display}");

            var settings = _settingsService.Settings;
            if (settings.SelectedModel != previousModel ||
                settings.Language != previousLanguage ||
                settings.ModelPath != previousModelPath)
            {
                previousModel = settings.SelectedModel;
                previousLanguage = settings.Language;
                previousModelPath = settings.ModelPath;
                await _transcriptionService!.ReloadAsync();
            }
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ExitApplication()
    {
        Shutdown(); // OnExit handles all cleanup
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _audioCaptureService?.Dispose();
        _transcriptionService?.Dispose();
        _recordingOverlay?.Close();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
