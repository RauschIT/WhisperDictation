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

        public ConfigManager(string configPath, ILogger? logger = null)
        {
            _configPath = configPath;
            _log = logger;
            _config = Load();
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
