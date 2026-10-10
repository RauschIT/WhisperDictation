// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace WhisperDictation.Core.Services
{
    public class WhisperService : IDisposable
    {
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private string _language = "de";
        private string _hotwords = "";

        public bool IsReady => _processor != null;

        public void Initialize(string modelPath, string language = "de", bool useGpu = true)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Whisper model not found.", modelPath);

            RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
            {
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.Cpu
            };

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string vulkanDir = Path.Combine(baseDir, "runtimes", "vulkan", "win-x64");
            if (Directory.Exists(vulkanDir))
            {
                foreach (var dll in Directory.GetFiles(vulkanDir, "ggml*.dll"))
                    NativeLibrary.TryLoad(dll, out _);
                string whisperDll = Path.Combine(vulkanDir, "whisper.dll");
                if (File.Exists(whisperDll))
                    NativeLibrary.TryLoad(whisperDll, out _);
            }

            Console.Write($"Loading model ({Path.GetFileName(modelPath)}, {(useGpu ? "GPU" : "CPU")})... ");
            var sw = Stopwatch.StartNew();

            // With UseGpu = false whisper.cpp uses the CPU backend even though the Vulkan library is loaded.
            _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu });
            _language = language;
            Rebuild();

            sw.Stop();
            Console.WriteLine($"OK ({sw.ElapsedMilliseconds}ms)");
        }

        public void SetHotwords(string hotwords)
        {
            _hotwords = hotwords ?? "";
            Rebuild();
        }

        public void SetLanguage(string language)
        {
            _language = string.IsNullOrEmpty(language) ? "de" : language;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_factory == null) return;
            var old = _processor;
            var builder = _factory.CreateBuilder()
                .WithLanguage(_language)
                .WithThreads(4);
            if (!string.IsNullOrEmpty(_hotwords))
                builder.WithPrompt(_hotwords);
            _processor = builder.Build();
            old?.Dispose();
        }

        public async Task<string?> TranscribeAsync(float[] audio, string? prompt = null)
        {
            if (_processor == null || audio.Length == 0) return null;

            await _lock.WaitAsync();
            try
            {
                // Disposed while waiting for the lock (model switch).
                if (_processor == null) return null;

                var pcm = new byte[audio.Length * 2];
                for (int i = 0; i < audio.Length; i++)
                {
                    short sample = (short)(Math.Clamp(audio[i], -1f, 1f) * 32767);
                    pcm[i * 2] = (byte)(sample & 0xFF);
                    pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }

                using var wavStream = BuildWavStream(pcm);
                string result = "";
                await foreach (var segment in _processor.ProcessAsync(wavStream))
                {
                    result += segment.Text;
                }
                return result.Trim();
            }
            finally
            {
                _lock.Release();
            }
        }

        private static MemoryStream BuildWavStream(byte[] pcmData)
        {
            var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + pcmData.Length);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(16000);
            bw.Write(32000);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(pcmData.Length);
            bw.Write(pcmData);
            ms.Position = 0;
            return ms;
        }

        // Waits for a running transcription, so the service can be replaced while dictating
        // (model or CPU/GPU switch). Later TranscribeAsync calls return null.
        public void Dispose()
        {
            _lock.Wait();
            try
            {
                if (_processor != null)
                {
                    _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    _processor = null;
                }
                _factory?.Dispose();
                _factory = null;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
