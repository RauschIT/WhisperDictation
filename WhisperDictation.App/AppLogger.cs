// Copyright (c) 2026 Rausch IT

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace WhisperDictation.App
{
    /// <summary>
    /// Logs to the debug console and to a log file at the same time.
    /// </summary>
    public class AppLogger : ILogger
    {
        private readonly string _category;
        private readonly StreamWriter _fileWriter;
        private readonly object _fileLock = new();

        public AppLogger(string category, StreamWriter fileWriter)
        {
            _category = category;
            _fileWriter = fileWriter;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string level = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???"
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string message = formatter(state, exception);
            string line = $"[{timestamp}] [{level}] {message}";

            if (exception != null)
                line += $"\n  {exception.GetType().Name}: {exception.Message}\n  {exception.StackTrace}";

            // Debug Console (VS Code "Debug Console" panel)
            Debug.WriteLine(line);

            // Logdatei
            lock (_fileLock)
            {
                try
                {
                    _fileWriter.WriteLine(line);
                    _fileWriter.Flush();
                }
                catch { }
            }
        }
    }

    public class AppLoggerProvider : ILoggerProvider
    {
        private readonly StreamWriter _fileWriter;

        public AppLoggerProvider(string logFilePath)
        {
            var dir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _fileWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
            _fileWriter.WriteLine($"\n=== Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        public ILogger CreateLogger(string categoryName) => new AppLogger(categoryName, _fileWriter);

        public void Dispose()
        {
            _fileWriter?.Dispose();
        }
    }
}
