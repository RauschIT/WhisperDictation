// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WhisperDictation.Core.Services
{
    public class SileroVadService : IDisposable
    {
        private InferenceSession? _session;
        private float[] _state = new float[2 * 1 * 128];
        private const int SAMPLE_RATE = 16000;
        private const int WINDOW_SIZE = 512; // 32ms at 16kHz

        public void Initialize(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Silero VAD model not found.", modelPath);

            var options = new SessionOptions();
            options.InterOpNumThreads = 1;
            options.IntraOpNumThreads = 1;
            _session = new InferenceSession(modelPath, options);

            ResetState();
        }

        public void ResetState()
        {
            Array.Clear(_state);
        }

        /// <summary>
        /// Returns the speech probability for one audio chunk (0.0 - 1.0).
        /// Audio must be float32, 16kHz, mono. Split into 512-sample windows.
        /// </summary>
        public float GetSpeechProbability(float[] audio)
        {
            if (_session == null || audio.Length == 0) return 0f;

            // Take the last full window
            int offset = Math.Max(0, audio.Length - WINDOW_SIZE);
            var chunk = new float[WINDOW_SIZE];
            int copyLen = Math.Min(WINDOW_SIZE, audio.Length);
            Array.Copy(audio, offset, chunk, WINDOW_SIZE - copyLen, copyLen);

            return RunInference(chunk);
        }

        /// <summary>
        /// Analyses audio and returns speech segments.
        /// </summary>
        public List<(int start, int end)> DetectSpeechSegments(
            float[] audio,
            float threshold = 0.5f,
            int minSilenceMs = 150,
            int minSpeechMs = 250)
        {
            ResetState();

            int minSilenceSamples = SAMPLE_RATE * minSilenceMs / 1000;
            int minSpeechSamples = SAMPLE_RATE * minSpeechMs / 1000;
            int minSilenceWindows = minSilenceSamples / WINDOW_SIZE;

            var segments = new List<(int start, int end)>();
            int speechStart = -1;
            int silenceCount = 0;

            for (int i = 0; i <= audio.Length - WINDOW_SIZE; i += WINDOW_SIZE)
            {
                var chunk = new float[WINDOW_SIZE];
                Array.Copy(audio, i, chunk, 0, WINDOW_SIZE);
                float prob = RunInference(chunk);

                if (prob >= threshold)
                {
                    if (speechStart < 0)
                        speechStart = i;
                    silenceCount = 0;
                }
                else if (speechStart >= 0)
                {
                    silenceCount++;
                    if (silenceCount >= minSilenceWindows)
                    {
                        int speechEnd = i - (silenceCount - 1) * WINDOW_SIZE;
                        if (speechEnd - speechStart >= minSpeechSamples)
                            segments.Add((speechStart, speechEnd));
                        speechStart = -1;
                        silenceCount = 0;
                    }
                }
            }

            // Last segment
            if (speechStart >= 0 && audio.Length - speechStart >= minSpeechSamples)
                segments.Add((speechStart, audio.Length));

            return segments;
        }

        private float RunInference(float[] chunk)
        {
            var inputTensor = new DenseTensor<float>(chunk, new[] { 1, chunk.Length });
            // Copy the state — DenseTensor references the array directly,
            // and _state is overwritten after the run
            var stateCopy = new float[_state.Length];
            Array.Copy(_state, stateCopy, _state.Length);
            var stateTensor = new DenseTensor<float>(stateCopy, new[] { 2, 1, 128 });
            var srTensor = new DenseTensor<long>(new long[] { SAMPLE_RATE }, new[] { 1 }); // Shape [1] wie in Python

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("state", stateTensor),
                NamedOnnxValue.CreateFromTensor("sr", srTensor),
            };

            using var results = _session!.Run(inputs);
            var output = results[0].AsTensor<float>();
            var stateN = results[1].AsTensor<float>();

            for (int i = 0; i < _state.Length; i++) _state[i] = stateN.GetValue(i);

            return output[0];
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
