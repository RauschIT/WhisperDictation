// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Thread-safe FIFO buffer for word output.
    /// The Whisper thread puts words in, the output thread types them with a configurable delay.
    /// </summary>
    public class WordOutputBuffer : IDisposable
    {
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly TextOutputService _output;
        private readonly Thread _outputThread;
        private volatile bool _running = true;
        private volatile int _delayMs = 60;

        public int DelayMs { get => _delayMs; set => _delayMs = value; }
        public int Pending => _queue.Count;

        public WordOutputBuffer(TextOutputService output)
        {
            _output = output;
            _outputThread = new Thread(OutputLoop)
            {
                IsBackground = true,
                Name = "WordOutput"
            };
            _outputThread.Start();
        }

        public void Enqueue(string text, bool prependSpace = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string prefix = (i == 0 && prependSpace) ? " " : (i > 0 ? " " : "");
                _queue.Enqueue(prefix + words[i]);
            }
        }

        private void OutputLoop()
        {
            while (_running)
            {
                if (_queue.TryDequeue(out string? word))
                {
                    _output.Type(word);
                    if (_delayMs > 0)
                        Thread.Sleep(_delayMs);
                }
                else
                {
                    Thread.Sleep(5);
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _outputThread.Join(2000);
        }
    }
}
