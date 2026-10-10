// Copyright (c) 2026 Rausch IT

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using WinForms = System.Windows.Forms;
using System.Windows.Media; // WPF Brushes
using WhisperDictation.Core;
using Color = System.Windows.Media.Color;

namespace WhisperDictation.App;

public partial class MainWindow : Window
{
    private DictationManager? _manager;
    private ConfigManager? _configManager;
    private AppLoggerProvider? _logProvider;
    private bool _loadingConfig;
    private WinForms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _iconIdle;
    private System.Drawing.Icon? _iconRecording;
    private bool _isExplicitClose;
    private Task? _initTask;

    private const string AppName = "Whisper Dictation";

    // From the VERSION file via Directory.Build.props; the SDK may append "+<commit>".
    private static readonly string AppVersion =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is [System.Reflection.AssemblyInformationalVersionAttribute attr, ..]
            ? attr.InformationalVersion : "").Split('+')[0];

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{AppName} v{AppVersion}";
        Loaded += OnLoaded;
        Deactivated += (s, e) => SaveHotwords();

        // Prepare the tray icon right away so the app is visible in the tray while initialisation runs.
        SetupTrayIcon();

        // Start hidden, but not minimised (avoids a tiny window in the lower left corner).
        this.Visibility = Visibility.Hidden;
        this.ShowInTaskbar = false;

        // Initialise the manager immediately — do not wait for Loaded, the window starts hidden.
        _initTask = Task.Run(InitializeManager);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Wait until the manager is initialised, then fill the sliders.
        if (_initTask != null)
            await _initTask;
        LoadConfigToUi();
    }

    private void LoadConfigToUi()
    {
        if (_configManager == null) return;
        _loadingConfig = true;
        var cfg = _configManager.Config;
        var hotkeyCombo = this.FindName("HotkeyModeCombo") as System.Windows.Controls.ComboBox;
        string hotkeyMode = string.Equals(cfg.HotkeyMode, "CtrlDoubleTap", StringComparison.OrdinalIgnoreCase)
            ? "CtrlDoubleTap"
            : "CtrlSpace";

        if (hotkeyCombo != null)
        {
            for (int i = 0; i < hotkeyCombo.Items.Count; i++)
            {
                if (hotkeyCombo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                    string.Equals(item.Tag as string, hotkeyMode, StringComparison.OrdinalIgnoreCase))
                {
                    hotkeyCombo.SelectedIndex = i;
                    break;
                }
            }
            if (hotkeyCombo.SelectedIndex < 0)
                hotkeyCombo.SelectedIndex = 0;
        }

        // Set the language combo box.
        var langCombo = this.FindName("LanguageCombo") as System.Windows.Controls.ComboBox;
        if (langCombo != null)
        {
            for (int i = 0; i < langCombo.Items.Count; i++)
            {
                if (langCombo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                    string.Equals(item.Tag as string, cfg.Language, StringComparison.OrdinalIgnoreCase))
                {
                    langCombo.SelectedIndex = i;
                    break;
                }
            }
            if (langCombo.SelectedIndex < 0)
                langCombo.SelectedIndex = 0;
        }

        // Model and device: show what is actually loaded, not only what the config asks for.
        ModelCombo.Items.Clear();
        if (_manager != null)
        {
            foreach (var model in _manager.AvailableModels)
                ModelCombo.Items.Add(model);
            ModelCombo.SelectedItem = _manager.CurrentModel;
        }
        bool useGpu = _manager?.CurrentUseGpu ?? cfg.UseGpu;
        DeviceCombo.SelectedIndex = useGpu ? 0 : 1;

        VadModeSlider.Value = cfg.VadMode;
        MinSilenceSlider.Value = cfg.MinSilenceMs;
        MinSpeechSlider.Value = cfg.MinSpeechMs;
        WordDelaySlider.Value = cfg.WordDelayMs;
        HotwordsText.Text = cfg.Hotwords;
        string[] vadLabels = { "Low", "Mid", "Hi", "Max" };
        VadModeValue.Text = vadLabels[Math.Clamp(cfg.VadMode, 0, 3)];
        MinSilenceValue.Text = $"{cfg.MinSilenceMs}";
        MinSpeechValue.Text = $"{cfg.MinSpeechMs}";
        WordDelayValue.Text = $"{cfg.WordDelayMs}";
        _loadingConfig = false;
    }

    private void OnLanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingConfig || _configManager == null) return;
        var langCombo = this.FindName("LanguageCombo") as System.Windows.Controls.ComboBox;
        if (langCombo == null) return;

        string lang = "de";
        if (langCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag)
        {
            lang = tag;
        }
        _configManager.Update(cfg => cfg.Language = lang);
        _manager?.ApplyConfig();
    }

    private async void OnWhisperSettingChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingConfig || _configManager == null || _manager == null) return;
        if (ModelCombo.SelectedItem is not string model) return;
        bool useGpu = (DeviceCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string != "cpu";

        _configManager.Update(cfg =>
        {
            cfg.WhisperModel = model;
            cfg.UseGpu = useGpu;
        });

        // Loading takes a few seconds; block further changes until it is done.
        ModelCombo.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
        try
        {
            await _manager.ReloadWhisperAsync();
        }
        catch (Exception ex)
        {
            _manager.Logger.LogError(ex, "Loading {Model} failed", model);

            // The previous model is still active: put config and UI back to it.
            _configManager.Update(cfg =>
            {
                cfg.WhisperModel = _manager.CurrentModel;
                cfg.UseGpu = _manager.CurrentUseGpu;
            });
            LoadConfigToUi();
            SetStatus($"Error: could not load {model}, still using {_manager.CurrentModel}.");
        }
        finally
        {
            ModelCombo.IsEnabled = true;
            DeviceCombo.IsEnabled = true;
        }
    }

    private void SetupTrayIcon()
    {
        if (_notifyIcon != null) return;

        _iconIdle = LoadTrayIcon("app-mic.ico");
        _iconRecording = LoadTrayIcon("app-mic-recording.ico");

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _iconIdle,
            Visible = true,
            Text = AppName
        };

        var contextMenu = new WinForms.ContextMenuStrip();
        var configItem = new WinForms.ToolStripMenuItem("Configuration", null, (s, e) => ShowWindow());
        configItem.Font = new System.Drawing.Font(configItem.Font, System.Drawing.FontStyle.Bold);
        
        contextMenu.Items.Add(configItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(new WinForms.ToolStripMenuItem("Quit", null, (s, e) => ExitApplication()));

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowWindow();
    }

    private async void ShowWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
        this.ShowInTaskbar = true;

        // Fill the sliders in case Loaded has not fired yet.
        if (_initTask != null)
        {
            await _initTask;
            LoadConfigToUi();
        }
    }

    private void ExitApplication()
    {
        _isExplicitClose = true;
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _iconIdle?.Dispose();
        _iconRecording?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void InitializeManager()
    {
        try
        {
            // Build the logger first, so that a message about missing models also reaches the log file.
            string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper_dictation.log");
            _logProvider = new AppLoggerProvider(logFile);
            var logger = _logProvider.CreateLogger("App");

            string? modelDir = ResolveModelDir(out string[] modelDiagnostics);
            if (modelDir == null)
            {
                foreach (var line in modelDiagnostics)
                    logger.LogError("{Line}", line);
                SetStatus("Error: models not found.");
                // The window has no log view; show the details so the cause is visible.
                Dispatcher.BeginInvoke(() => System.Windows.MessageBox.Show(this,
                    string.Join(Environment.NewLine, modelDiagnostics),
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error));
                return;
            }

            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _configManager = new ConfigManager(configPath, logger);

            _manager = new DictationManager(modelDir, _configManager, logger);
            _manager.OnStatusChanged += status => SetStatus(status);
            _manager.OnRecordingStatusChanged += recording => UpdateTrayIcon(recording);
            _manager.Initialize();
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
    }

    private void UpdateTrayIcon(bool recording)
    {
        Dispatcher.Invoke(() =>
        {
            if (_notifyIcon == null) return;
            _notifyIcon.Icon = recording ? _iconRecording : _iconIdle;
            _notifyIcon.Text = recording ? "Whisper: RECORDING" : "Whisper: Ready";
        });
    }

    private string? ResolveModelDir(out string[] diagnostics)
    {
        const string REQUIRED_MODEL = "silero_vad.onnx";
        var diag = new List<string>();

        string[] candidates = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "WhisperDictation.Core", "models")),
            Path.Combine(Directory.GetCurrentDirectory(), "models"),
        };

        // A directory alone is not enough — it must also contain the VAD model,
        // otherwise start-up aborts without any visible reason.
        foreach (var dir in candidates)
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, REQUIRED_MODEL)))
            {
                diagnostics = diag.ToArray();
                return dir;
            }

        // Nothing found: report it clearly, otherwise the application
        // looks broken instead of looking like "models are missing".
        diag.Add("Models not found. Searched in:");
        foreach (var dir in candidates)
            diag.Add($"  {dir}   ({(File.Exists(Path.Combine(dir, REQUIRED_MODEL)) ? "present" : "missing")})");
        diag.Add($"Required: {REQUIRED_MODEL}, plus a Whisper model (default: ggml-large-v3.bin).");
        diag.Add("Download links are in README.md, section \"Download the models\".");

        diagnostics = diag.ToArray();
        return null;
    }

    private void OnParameterChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _loadingConfig || _configManager == null) return;

        string[] vadLabels = { "Low", "Mid", "Hi", "Max" };
        VadModeValue.Text = vadLabels[(int)VadModeSlider.Value];
        MinSilenceValue.Text = $"{(int)MinSilenceSlider.Value}";
        MinSpeechValue.Text = $"{(int)MinSpeechSlider.Value}";
        WordDelayValue.Text = $"{(int)WordDelaySlider.Value}";

        _configManager.Update(cfg =>
        {
            cfg.VadMode = (int)VadModeSlider.Value;
            cfg.MinSilenceMs = (int)MinSilenceSlider.Value;
            cfg.MinSpeechMs = (int)MinSpeechSlider.Value;
            cfg.WordDelayMs = (int)WordDelaySlider.Value;
        });

        _manager?.ApplyConfig();
    }

    private void OnHotwordsChanged(object sender, RoutedEventArgs e) => SaveHotwords();

    // LostFocus alone is not enough: closing (hiding) the window or switching to another
    // application does not move the focus out of the text box, so the text would be lost
    // and overwritten by LoadConfigToUi the next time the window opens.
    private void SaveHotwords()
    {
        if (_configManager == null || HotwordsText.Text == _configManager.Config.Hotwords) return;
        _configManager.Update(cfg => cfg.Hotwords = HotwordsText.Text);
        _manager?.ApplyConfig();
    }

    private void OnHotkeyModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingConfig || _configManager == null) return;
        var hotkeyCombo = this.FindName("HotkeyModeCombo") as System.Windows.Controls.ComboBox;
        if (hotkeyCombo == null) return;

        string mode = "CtrlSpace";
        if (hotkeyCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag &&
            string.Equals(tag, "CtrlDoubleTap", StringComparison.OrdinalIgnoreCase))
        {
            mode = "CtrlDoubleTap";
        }

        _configManager.Update(cfg => cfg.HotkeyMode = mode);
        _manager?.ApplyConfig();
    }

    private void SetStatus(string text)
    {
        // The window starts hidden. On an error it has to become visible,
        // otherwise the application looks as if it never started.
        if (text.StartsWith("Error"))
        {
            Dispatcher.BeginInvoke(() =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
                this.ShowInTaskbar = true;
            });
        }

        Dispatcher.BeginInvoke(() =>
        {
            bool active = text == "AKTIV";
            bool error = text.StartsWith("Error");

            StatusText.Text = text;
            if (active)
            {
                StatusText.Foreground = System.Windows.Media.Brushes.White;
                (StatusText.Parent as System.Windows.Controls.Border)!.Background =
                    new SolidColorBrush(Color.FromRgb(0, 200, 80));
            }
            else if (error)
            {
                StatusText.Foreground = System.Windows.Media.Brushes.White;
                (StatusText.Parent as System.Windows.Controls.Border)!.Background =
                    new SolidColorBrush(Color.FromRgb(204, 0, 0));
            }
            else
            {
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215));
                (StatusText.Parent as System.Windows.Controls.Border)!.Background =
                    new SolidColorBrush(Color.FromRgb(232, 232, 232));
            }
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveHotwords();
        if (!_isExplicitClose)
        {
            e.Cancel = true;
            this.Hide();
            this.ShowInTaskbar = false;
        }
        else
        {
            _manager?.Dispose();
            _logProvider?.Dispose();
            base.OnClosing(e);
        }
    }

    private static System.Drawing.Icon LoadTrayIcon(string fileName)
    {
        // The .ico holds several sizes; take the one matching the tray (16 px at 100 %, larger at higher DPI).
        var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{fileName}"));
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }
}
