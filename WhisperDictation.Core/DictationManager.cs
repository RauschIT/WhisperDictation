// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhisperDictation.Core.Services;

namespace WhisperDictation.Core
{
    public class DictationManager : IDisposable
    {
        private readonly ILogger _log;
        private readonly ConfigManager _configManager;
        private readonly string _modelDir;

        private WhisperWorkerClient? _whisper;
        private SpeechSegmentDetector? _detector;
        private HotkeyService? _hotkey;
        private TextOutputService? _output;
        private WordOutputBuffer? _wordBuffer;
        private string _currentHotkeyMode = "CtrlSpace";
        private string _currentLanguage = "de";
        private string _currentModel = "";
        private bool _currentUseGpu = true;

        // Preferred when the configured model is missing: best quality first.
        private const string PreferredModel = "ggml-large-v3.bin";

        private volatile bool _active;
        private string _previousText = "";

        public event Action<string>? OnStatusChanged;
        public event Action<string>? OnTextOutput;
        public event Action<bool>? OnRecordingStatusChanged;

        public bool IsActive => _active;
        public bool IsReady => _whisper?.IsReady ?? false;
        public ConfigManager ConfigManager => _configManager;

        /// <summary>The Whisper model that is actually loaded (file name in the model folder).</summary>
        public string CurrentModel => _currentModel;
        /// <summary>Whether the loaded model was requested on the GPU.</summary>
        public bool CurrentUseGpu => _currentUseGpu;

        /// <summary>Whisper model files (ggml-*.bin) present in the model folder, sorted by name.</summary>
        public IReadOnlyList<string> AvailableModels
        {
            get
            {
                if (!Directory.Exists(_modelDir)) return Array.Empty<string>();
                var names = new List<string>();
                foreach (var file in Directory.GetFiles(_modelDir, "ggml-*.bin"))
                    names.Add(Path.GetFileName(file));
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names;
            }
        }

        private static readonly Regex HallucinationPhraseRegex = new(
            @"^(?:vielen dank|danke|danke schön|danke sehr|tschüss|tschüs|ciao|auf wiedersehen|amen|thank you|thanks|bye|goodbye|vielen dank für's zuhören|vielen dank fürs zuschauen|thank you for watching|thanks for watching|untertitel von stephanie gündel|untertitel im auftrag des zdf|copyright watchzilla|Render|srt by(?:\b.*)?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AsteriskStageDirectionRegex = new(
            @"^\*+\s*[^*].*?\s*\*+$",
            RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public DictationManager(string modelDir, ConfigManager configManager, ILogger? logger = null)
        {
            _modelDir = modelDir;
            _configManager = configManager;
            _log = logger ?? CreateDefaultLogger();
        }

        public ILogger Logger => _log;

        private static ILogger CreateDefaultLogger()
        {
            var factory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
            return factory.CreateLogger("Dictation");
        }

        public void Initialize()
        {
            var cfg = _configManager.Config;
            string vadPath = Path.Combine(_modelDir, "silero_vad.onnx");

            if (!File.Exists(vadPath))
                throw new FileNotFoundException($"VAD model not found: {vadPath}");

            string model = ResolveModel(cfg.WhisperModel);
            if (!string.Equals(model, cfg.WhisperModel, StringComparison.OrdinalIgnoreCase))
            {
                // E.g. only the small model was installed: use it instead of failing.
                _log.LogWarning("Whisper model {Configured} not found, using {Model}", cfg.WhisperModel, model);
                _configManager.Update(c => c.WhisperModel = model);
            }

            _whisper = LoadWhisper(model, cfg.UseGpu);
            _currentLanguage = cfg.Language;

            _output = new TextOutputService();
            _wordBuffer = new WordOutputBuffer(_output);
            _wordBuffer.DelayMs = cfg.WordDelayMs;

            _currentHotkeyMode = NormalizeHotkeyMode(cfg.HotkeyMode);
            _hotkey = new HotkeyService();
            _hotkey.OnToggle += Toggle;
            _hotkey.Start(_currentHotkeyMode);
            _log.LogInformation("Hotkey registered ({Mode})", GetHotkeyLabel(_currentHotkeyMode));

            OnStatusChanged?.Invoke($"Ready. {GetHotkeyLabel(_currentHotkeyMode)} to dictate.");
        }

        /// <summary>
        /// Returns the configured model if it exists, otherwise the preferred model, otherwise
        /// the first model found. Throws when the folder contains no Whisper model at all.
        /// </summary>
        private string ResolveModel(string configured)
        {
            var models = AvailableModels;
            foreach (var name in new[] { configured, PreferredModel })
                foreach (var m in models)
                    if (string.Equals(m, name, StringComparison.OrdinalIgnoreCase))
                        return m;
            if (models.Count > 0)
                return models[0];
            throw new FileNotFoundException($"No Whisper model (ggml-*.bin) found in {_modelDir}");
        }

        private WhisperWorkerClient LoadWhisper(string model, bool useGpu)
        {
            var cfg = _configManager.Config;
            string path = Path.Combine(_modelDir, model);
            _log.LogInformation("Loading Whisper from {Path} on {Device}", path, useGpu ? "GPU" : "CPU");

            var whisper = new WhisperWorkerClient();
            whisper.Initialize(path, cfg.Language, useGpu);
            if (!string.IsNullOrEmpty(cfg.Hotwords))
                whisper.SetHotwords(cfg.Hotwords);

            _currentModel = model;
            _currentUseGpu = useGpu;
            _log.LogInformation("Whisper loaded (worker process {Pid})", whisper.ProcessId);
            return whisper;
        }

        /// <summary>
        /// Loads the model and device from the config if they differ from the loaded ones.
        /// The previous model keeps working until the new one is loaded; if loading fails,
        /// it stays active and the exception is passed on.
        /// </summary>
        public Task ReloadWhisperAsync()
        {
            return Task.Run(() =>
            {
                var cfg = _configManager.Config;
                if (string.Equals(cfg.WhisperModel, _currentModel, StringComparison.OrdinalIgnoreCase) &&
                    cfg.UseGpu == _currentUseGpu)
                    return;

                OnStatusChanged?.Invoke($"Loading {cfg.WhisperModel} ({(cfg.UseGpu ? "GPU" : "CPU")})...");

                // LoadWhisper only updates CurrentModel/CurrentUseGpu once loading succeeded.
                var loaded = LoadWhisper(cfg.WhisperModel, cfg.UseGpu);
                var old = Interlocked.Exchange(ref _whisper, loaded);
                if (old != null)
                {
                    // Ending the old worker process frees its model, RAM and GPU memory completely.
                    int oldPid = old.ProcessId;
                    old.Dispose();
                    _log.LogInformation("Whisper worker process {Pid} stopped", oldPid);
                }
                OnStatusChanged?.Invoke(_active ? "AKTIV" : $"Ready. {GetHotkeyLabel(_currentHotkeyMode)} to dictate.");
            });
        }

        /// <summary>
        /// Maps the UI value "noise suppression" (0..3) onto a WebRTC mode plus Silero strictness.
        /// WebRTC only knows 4 discrete modes. The highest step (3) deliberately does not use
        /// the too aggressive WebRTC mode 3 (it cuts off speech), but WebRTC 2 plus a strict
        /// Silero check — a step between "Hi" and the old "Max".
        /// </summary>
        private static (int webRtcMode, double sileroRatio) MapVadMode(int vadMode)
        {
            if (vadMode <= 2)
                return (Math.Clamp(vadMode, 0, 2), 0.0);
            return (2, 0.35);
        }

        private static string NormalizeHotkeyMode(string? mode)
        {
            if (string.Equals(mode, "CtrlDoubleTap", StringComparison.OrdinalIgnoreCase))
                return "CtrlDoubleTap";
            return "CtrlSpace";
        }

        private static string GetHotkeyLabel(string mode)
        {
            return mode == "CtrlDoubleTap" ? "Double-Tap CTRL" : "CTRL+SPACE";
        }

        public void Toggle()
        {
            if (!IsReady) return;
            _active = !_active;
            if (_active)
                Task.Run(() => StartRecording());
            else
                StopRecording();
        }

        private void StartRecording()
        {
            try
            {
                var cfg = _configManager.Config;
                string sileroPath = Path.Combine(_modelDir, "silero_vad.onnx");
                var (webRtcMode, sileroRatio) = MapVadMode(cfg.VadMode);
                _detector = new SpeechSegmentDetector(webRtcMode, sileroModelPath: sileroPath);
                _detector.SileroSpeechRatio = sileroRatio;
                _detector.MinSilenceChunks = cfg.MinSilenceMs / 32;
                _detector.MinSpeechSamples = cfg.MinSpeechMs * 16;
                _detector.StartSpeechFrames = cfg.StartSpeechFrames;
                _detector.OnSpeechSegment += OnSpeechSegment;

                // _detector.DebugAudioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_audio");
                // Directory.CreateDirectory(_detector.DebugAudioPath);
                _log.LogInformation("Detector: VadMode={Mode}, SilenceChunks={Sil}, SpeechSamples={Sp}, StartFrames={Start}",
                    cfg.VadMode, _detector.MinSilenceChunks, _detector.MinSpeechSamples, _detector.StartSpeechFrames);

                _detector.Start();
                _previousText = "";
                _log.LogInformation("Recording started");
                OnStatusChanged?.Invoke("AKTIV");
                OnRecordingStatusChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Starting the recording failed");
                _active = false;
                OnStatusChanged?.Invoke("Error: " + ex.Message);
            }
        }

        private void StopRecording()
        {
            _detector?.Stop();
            _detector?.Dispose();
            _detector = null;
            _log.LogInformation("Recording stopped");
            OnStatusChanged?.Invoke("INAKTIV");
            OnRecordingStatusChanged?.Invoke(false);
        }

        private void OnSpeechSegment(float[] audio)
        {
            var cfg = _configManager.Config;
            float durationMs = audio.Length / 16f;
            _log.LogDebug("Segment: {Duration:F0}ms", durationMs);

            Task.Run(async () =>
            {
                try
                {
                    string? text = await _whisper!.TranscribeAsync(audio, _previousText);
                    if (string.IsNullOrEmpty(text)) return;

                    string cleaned = text.Trim().TrimEnd('.', '!', '?', ',', ';', ':');
                    if (IsHallucination(cleaned))
                    {
                        _log.LogDebug("Hallucination: '{Text}'", text);
                        return;
                    }

                    bool prependSpace = !string.IsNullOrEmpty(_previousText);
                    _wordBuffer!.Enqueue(text, prependSpace);
                    _previousText = text;
                    _log.LogInformation("Output: '{Text}'", text.Trim());
                    OnTextOutput?.Invoke(text.Trim());
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Transcription failed");
                }
            });
        }

        private static bool IsHallucination(string cleaned)
        {
            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            return HallucinationPhraseRegex.IsMatch(cleaned) || AsteriskStageDirectionRegex.IsMatch(cleaned);
        }

        public void ApplyConfig()
        {
            var cfg = _configManager.Config;
            if (_detector != null)
            {
                // WebRTC VAD has no threshold — it is controlled through VadMode
                _detector.MinSilenceChunks = cfg.MinSilenceMs / 32;
                _detector.MinSpeechSamples = cfg.MinSpeechMs * 16;
                _detector.StartSpeechFrames = cfg.StartSpeechFrames;
                _log.LogInformation("Detector update: SilenceChunks={Sil}, SpeechSamples={Sp}, StartFrames={Start}",
                    _detector.MinSilenceChunks, _detector.MinSpeechSamples, _detector.StartSpeechFrames);
            }
            _whisper?.SetHotwords(cfg.Hotwords ?? "");
            if (_wordBuffer != null)
                _wordBuffer.DelayMs = cfg.WordDelayMs;

            string newLanguage = string.IsNullOrEmpty(cfg.Language) ? "de" : cfg.Language;
            if (!string.Equals(newLanguage, _currentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _whisper?.SetLanguage(newLanguage);
                _currentLanguage = newLanguage;
                _log.LogInformation("Language switched to {Language}", newLanguage);
            }

            string newMode = NormalizeHotkeyMode(cfg.HotkeyMode);
            if (newMode != _currentHotkeyMode)
            {
                _hotkey?.Dispose();
                _hotkey = new HotkeyService();
                _hotkey.OnToggle += Toggle;
                _hotkey.Start(newMode);
                _currentHotkeyMode = newMode;
                _log.LogInformation("Hotkey switched to {Mode}", GetHotkeyLabel(_currentHotkeyMode));
                OnStatusChanged?.Invoke($"Ready. {GetHotkeyLabel(_currentHotkeyMode)} to dictate.");
            }
        }

        public void Dispose()
        {
            StopRecording();
            _wordBuffer?.Dispose();
            _whisper?.Dispose();
            _hotkey?.Dispose();
        }
    }
}
