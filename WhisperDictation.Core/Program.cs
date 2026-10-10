// Copyright (c) 2026 Rausch IT

using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WhisperDictation.Core.Services;

/// <summary>
/// Audio tokenizer plus Whisper transcription.
/// Detects speech segments and transcribes them one by one.
/// </summary>
class Program
{
    const int SAMPLE_RATE = 16000;

    // Segmentierung
    const float SILENCE_THRESHOLD = 0.005f;
    const int MIN_SILENCE_MS = 150;
    const int MIN_SEGMENT_MS = 200;

    static float[] LoadWav(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12); // RIFF header
        while (true)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkId == "data")
            {
                var samples = new float[chunkSize / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = reader.ReadInt16() / 32768f;
                return samples;
            }
            reader.ReadBytes(chunkSize);
        }
    }

    static float[] Resample(float[] audio, int fromRate, int toRate)
    {
        if (fromRate == toRate) return audio;
        double ratio = (double)toRate / fromRate;
        var result = new float[(int)(audio.Length * ratio)];
        for (int i = 0; i < result.Length; i++)
        {
            double srcIdx = i / ratio;
            int idx = (int)srcIdx;
            float frac = (float)(srcIdx - idx);
            if (idx + 1 < audio.Length)
                result[i] = audio[idx] * (1 - frac) + audio[idx + 1] * frac;
            else
                result[i] = audio[Math.Min(idx, audio.Length - 1)];
        }
        return result;
    }

    static int GetWavSampleRate(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(24);
        return reader.ReadInt32();
    }

    /// <summary>
    /// Finds speech segments based on RMS energy.
    /// </summary>
    static List<(int start, int end)> FindSegments(float[] audio)
    {
        int windowSize = SAMPLE_RATE / 100; // 10ms
        int minSilenceWindows = SAMPLE_RATE * MIN_SILENCE_MS / 1000 / windowSize;
        int minSegmentSamples = SAMPLE_RATE * MIN_SEGMENT_MS / 1000;
        int numWindows = audio.Length / windowSize;

        // Energy per window
        var energies = new float[numWindows];
        for (int w = 0; w < numWindows; w++)
        {
            float e = 0;
            for (int j = w * windowSize; j < (w + 1) * windowSize; j++)
                e += audio[j] * audio[j];
            energies[w] = MathF.Sqrt(e / windowSize);
        }

        // State Machine
        var segments = new List<(int start, int end)>();
        int speechStart = -1;
        int silenceCount = 0;

        for (int w = 0; w < numWindows; w++)
        {
            if (energies[w] >= SILENCE_THRESHOLD)
            {
                if (speechStart < 0)
                    speechStart = w * windowSize;
                silenceCount = 0;
            }
            else if (speechStart >= 0)
            {
                silenceCount++;
                if (silenceCount >= minSilenceWindows)
                {
                    int speechEnd = (w - silenceCount + 1) * windowSize;
                    if (speechEnd - speechStart >= minSegmentSamples)
                        segments.Add((speechStart, speechEnd));
                    speechStart = -1;
                    silenceCount = 0;
                }
            }
        }

        if (speechStart >= 0 && audio.Length - speechStart >= minSegmentSamples)
            segments.Add((speechStart, audio.Length));

        return segments;
    }

    static async Task RunBenchmark(string wavFile)
    {
        if (!File.Exists(wavFile)) { Console.WriteLine($"Not found: {wavFile}"); return; }

        int srcRate = GetWavSampleRate(wavFile);
        float[] audio = LoadWav(wavFile);
        if (srcRate != SAMPLE_RATE) audio = Resample(audio, srcRate, SAMPLE_RATE);
        float totalSec = audio.Length / (float)SAMPLE_RATE;
        Console.WriteLine($"Audio: {totalSec:F1}s\n");

        string modelPath = Path.Combine("models", "ggml-large-v3.bin");
        if (!File.Exists(modelPath)) modelPath = Path.Combine("..", "WhisperDictation.Core", "models", "ggml-large-v3.bin");
        // Beide Modelle vergleichen
        foreach (string model in new[] { "ggml-large-v3.bin", "ggml-small.bin" })
        {
            string mPath = Path.Combine(Path.GetDirectoryName(modelPath)!, model);
            if (!File.Exists(mPath)) { Console.WriteLine($"  {model}: not found\n"); continue; }

            var whisper = new WhisperService();
            whisper.Initialize(mPath);
            Console.WriteLine();

            foreach (float chunkSec in new[] { 1f, 2f, 3f, 5f, 7f, 10f, totalSec })
        {
            float sec = Math.Min(chunkSec, totalSec);
            int samples = (int)(sec * SAMPLE_RATE);
            float[] segment = audio[..samples];

            var sw = Stopwatch.StartNew();
            string? text = await whisper.TranscribeAsync(segment);
            sw.Stop();

            Console.WriteLine($"  {sec,5:F1}s ({sw.ElapsedMilliseconds,4}ms): '{text}'");
        }

        whisper.Dispose();
        Console.WriteLine();
        }
    }

    static async Task Main(string[] args)
    {
        // Live-Modus
        if (args.Length > 0 && args[0] == "--live")
        {
            string modelDir = args.Length > 1 ? args[1] : "models";
            string configPath = Path.Combine(modelDir, "..", "config.json");
            var logFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole());
            var logger = logFactory.CreateLogger("Console");
            var configManager = new WhisperDictation.Core.ConfigManager(configPath, logger);
            using var manager = new WhisperDictation.Core.DictationManager(modelDir, configManager, logger);
            manager.Initialize();
            string hotkeyLabel = string.Equals(configManager.Config.HotkeyMode, "CtrlDoubleTap", StringComparison.OrdinalIgnoreCase)
                ? "Double-Tap CTRL"
                : "CTRL+SPACE";
            Console.WriteLine($"Ready. {hotkeyLabel} to dictate.");
            while (true) System.Threading.Thread.Sleep(100);
        }

        // VAD test on a WAV file — compares Silero and WebRTC
        if (args.Length > 0 && args[0] == "--vad-test")
        {
            string wavPath = args.Length > 1 ? args[1] : @"..\voices\live_test_2.wav";
            string vadModel = Path.Combine("models", "silero_vad.onnx");
            if (!File.Exists(vadModel)) vadModel = Path.Combine("..", "WhisperDictation.Core", "models", "silero_vad.onnx");

            int sr = GetWavSampleRate(wavPath);
            float[] wav = LoadWav(wavPath);
            if (sr != SAMPLE_RATE) wav = Resample(wav, sr, SAMPLE_RATE);
            Console.WriteLine($"Audio: {wav.Length / (float)SAMPLE_RATE:F1}s\n");

            // Silero VAD
            Console.WriteLine("=== Silero VAD ===");
            var silero = new SileroVadService();
            silero.Initialize(vadModel);
            int sileroSpeech = 0, sileroSilence = 0;
            float? sileroSegStart = null;
            int sileroSilCount = 0;
            for (int i = 0; i <= wav.Length - 512; i += 512)
            {
                var chunk = new float[512];
                Array.Copy(wav, i, chunk, 0, 512);
                float prob = silero.GetSpeechProbability(chunk);
                float t = i / (float)SAMPLE_RATE;
                if (prob > 0.4f)
                {
                    sileroSpeech++;
                    if (sileroSegStart == null) sileroSegStart = t;
                    sileroSilCount = 0;
                }
                else
                {
                    sileroSilence++;
                    if (sileroSegStart != null)
                    {
                        sileroSilCount++;
                        if (sileroSilCount >= 5)
                        {
                            Console.WriteLine($"  [{sileroSegStart:F1}s - {t:F1}s]");
                            sileroSegStart = null;
                            sileroSilCount = 0;
                        }
                    }
                }
            }
            if (sileroSegStart != null) Console.WriteLine($"  [{sileroSegStart:F1}s - end]");
            Console.WriteLine($"  Speech: {sileroSpeech}, Silence: {sileroSilence}\n");
            silero.Dispose();

            // WebRTC VAD
            Console.WriteLine("=== WebRTC VAD ===");
            var webrtc = new WebRtcVadService();
            webrtc.Initialize();
            int webrtcSpeech = 0, webrtcSilence = 0;
            float? webrtcSegStart = null;
            int webrtcSilCount = 0;
            int frameSamples = WebRtcVadService.FRAME_SAMPLES;
            for (int i = 0; i <= wav.Length - frameSamples; i += frameSamples)
            {
                var chunk = new float[frameSamples];
                Array.Copy(wav, i, chunk, 0, frameSamples);
                bool speech = webrtc.HasSpeech(chunk);
                float t = i / (float)SAMPLE_RATE;
                if (speech)
                {
                    webrtcSpeech++;
                    if (webrtcSegStart == null) webrtcSegStart = t;
                    webrtcSilCount = 0;
                }
                else
                {
                    webrtcSilence++;
                    if (webrtcSegStart != null)
                    {
                        webrtcSilCount++;
                        if (webrtcSilCount >= 5)
                        {
                            Console.WriteLine($"  [{webrtcSegStart:F1}s - {t:F1}s]");
                            webrtcSegStart = null;
                            webrtcSilCount = 0;
                        }
                    }
                }
            }
            if (webrtcSegStart != null) Console.WriteLine($"  [{webrtcSegStart:F1}s - end]");
            Console.WriteLine($"  Speech: {webrtcSpeech}, Silence: {webrtcSilence}");
            webrtc.Dispose();
            return;
        }

        // Benchmark: compare segment lengths
        if (args.Length > 0 && args[0] == "--benchmark")
        {
            string wavPath = args.Length > 1 ? args[1] : @"..\voices\micha-test-3.wav";
            await RunBenchmark(wavPath);
            return;
        }

        string wavFile = args.Length > 0 ? args[0] : @"..\voices\micha-test-3.wav";

        if (!File.Exists(wavFile))
        {
            Console.WriteLine($"File not found: {wavFile}");
            return;
        }

        int srcRate = GetWavSampleRate(wavFile);
        float[] audio = LoadWav(wavFile);

        if (srcRate != SAMPLE_RATE)
            audio = Resample(audio, srcRate, SAMPLE_RATE);

        Console.WriteLine($"Audio: {audio.Length / (float)SAMPLE_RATE:F1}s");

        // Silero VAD fuer Sprachsegment-Erkennung
        string vadPath = Path.Combine(
            Path.GetDirectoryName(typeof(Program).Assembly.Location)!,
            "models", "silero_vad.onnx");
        if (!File.Exists(vadPath))
            vadPath = Path.Combine("models", "silero_vad.onnx");

        List<(int start, int end)> segments;
        if (File.Exists(vadPath))
        {
            var vad = new SileroVadService();
            vad.Initialize(vadPath);
            segments = vad.DetectSpeechSegments(audio, threshold: 0.5f, minSilenceMs: 150, minSpeechMs: 250);
            vad.Dispose();
            Console.WriteLine($"Segments (Silero VAD): {segments.Count}\n");
        }
        else
        {
            Console.WriteLine("Silero VAD not found, using the energy fallback.");
            segments = FindSegments(audio);
            Console.WriteLine($"Segments (energy): {segments.Count}\n");
        }

        // Only list the segments plus the energy profile
        foreach (var (start, end) in segments)
        {
            float startSec = start / (float)SAMPLE_RATE;
            float endSec = end / (float)SAMPLE_RATE;
            float durMs = (end - start) / (float)SAMPLE_RATE * 1000;
            Console.WriteLine($"  [{startSec,6:F2}s - {endSec,6:F2}s] ({durMs,5:F0}ms)");
        }

        // Whisper
        string modelPath = Path.Combine(
            Path.GetDirectoryName(typeof(Program).Assembly.Location)!,
            "models", "ggml-large-v3.bin");
        if (!File.Exists(modelPath))
            modelPath = Path.Combine("models", "ggml-large-v3.bin");

        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"\nModel not found. Segmentation only.");
            return;
        }

        Console.WriteLine();
        var whisper = new WhisperService();
        whisper.Initialize(modelPath);
        whisper.SetHotwords("Begriffe: Script, Skript, Python, API, async, await, Debugger, Claude, Vulkan.");
        Console.WriteLine();

        string previousText = "";
        var totalSw = Stopwatch.StartNew();
        foreach (var (start, end) in segments)
        {
            float startSec = start / (float)SAMPLE_RATE;
            float endSec = end / (float)SAMPLE_RATE;

            int pad = SAMPLE_RATE * 300 / 1000;
            int padStart = Math.Max(0, start - pad);
            int padEnd = Math.Min(audio.Length, end + pad);
            float[] segment = audio[padStart..padEnd];

            var sw = Stopwatch.StartNew();
            string? text = await whisper.TranscribeAsync(segment, previousText);
            sw.Stop();

            Console.WriteLine($"  [{startSec,5:F2}s-{endSec,5:F2}s] {sw.ElapsedMilliseconds,4}ms  '{text}'");
            if (!string.IsNullOrEmpty(text))
                previousText = text;
        }
        totalSw.Stop();
        Console.WriteLine($"\nGesamt: {totalSw.ElapsedMilliseconds}ms");

        whisper.Dispose();
    }
}
