// Copyright (c) 2026 Rausch IT

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.Core
{
    public class ConfigManager
    {
        private readonly string _configPath;
        private DictationConfig _config;
        private ILogger? _log;

        public DictationConfig Config => _config;

        /// <summary>Default location: %APPDATA%\WhisperDictation\config.json (user directory, survives reinstalls).</summary>
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhisperDictation", "config.json");

        /// <param name="legacyPath">Old config next to the exe; copied once if the new file does not exist yet.</param>
        public ConfigManager(string configPath, ILogger? logger = null, string? legacyPath = null)
        {
            _configPath = configPath;
            _log = logger;
            Migrate(legacyPath);
            _config = Load();
        }

        private void Migrate(string? legacyPath)
        {
            try
            {
                if (legacyPath == null || File.Exists(_configPath) || !File.Exists(legacyPath))
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.Copy(legacyPath, _configPath);
                _log?.LogInformation("Config migrated from {Old} to {New}", legacyPath, _configPath);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Migrating the old config failed");
            }
        }

        private DictationConfig Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<DictationConfig>(json);
                    if (config != null)
                    {
                        _log?.LogInformation("Config loaded: {Path}", _configPath);
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Loading the config failed, using defaults");
            }
            _log?.LogInformation("Created a new config with defaults");
            return new DictationConfig();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Saving the config failed");
            }
        }

        public void Update(Action<DictationConfig> modifier)
        {
            modifier(_config);
            Save();
            _log?.LogDebug("Config updated: VadMode={Mode}, Silence={Sil}ms, Speech={Sp}ms, WordDelay={Delay}ms",
                _config.VadMode, _config.MinSilenceMs, _config.MinSpeechMs, _config.WordDelayMs);
        }
    }
}
