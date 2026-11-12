// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Generic;
using System.IO;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Push-to-talk recording. The microphone runs all the time into a short ring buffer
    /// that only lives in memory and is overwritten continuously. A clip starts with the
    /// last part of that buffer (pre-roll), so words spoken while the key goes down are
    /// kept, and runs until it is ended. The whole clip is passed on in one piece; there is
    /// no pause detection that could cut it.
    /// </summary>
    public class PushToTalkRecorder : IDisposable
    {
        private const int SAMPLE_RATE = 16000;
        private const int RING_SECONDS = 3;
        private const int SILERO_WINDOW = SileroVadOnnxModel.WINDOW_SIZE_16K;
        private const float SILERO_THRESHOLD = 0.5f;

        private readonly IAudioCapture _capture;
        private readonly SileroVadOnnxModel? _silero;
        private readonly object _lock = new();
        private readonly object _sileroLock = new();

        private readonly float[] _ring = new float[SAMPLE_RATE * RING_SECONDS];
        private int _ringPos;
        private int _ringFilled;

        private List<float>? _clip;
        private bool _started;

        public PushToTalkRecorder(string? sileroModelPath = null, IAudioCapture? capture = null)
        {
            if (!string.IsNullOrEmpty(sileroModelPath) && File.Exists(sileroModelPath))
                _silero = new SileroVadOnnxModel(sileroModelPath);
            _capture = capture ?? new NAudioCapture();
        }

        /// <summary>Opens the microphone and starts filling the ring buffer.</summary>
        public void StartListening()
        {
            if (_started) return;
            _capture.Start(OnAudioData);
            _started = true;
        }

        private void OnAudioData(float[] samples)
        {
            lock (_lock)
            {
                foreach (float s in samples)
                {
                    _ring[_ringPos] = s;
                    _ringPos = (_ringPos + 1) % _ring.Length;
                }
                _ringFilled = Math.Min(_ring.Length, _ringFilled + samples.Length);
                _clip?.AddRange(samples);
            }
        }

        /// <summary>Starts a clip with the last <paramref name="preRollMs"/> of the ring buffer.</summary>
        public void BeginClip(int preRollMs)
        {
            lock (_lock)
            {
                int count = Math.Min(_ringFilled, preRollMs * SAMPLE_RATE / 1000);
                var clip = new List<float>(count + SAMPLE_RATE * 10);
                int start = (_ringPos - count + _ring.Length) % _ring.Length;
                for (int i = 0; i < count; i++)
                    clip.Add(_ring[(start + i) % _ring.Length]);
                _clip = clip;
            }
        }

        /// <summary>Ends the clip and returns its audio (empty if no clip was running).</summary>
        public float[] EndClip()
        {
            lock (_lock)
            {
                var clip = _clip;
                _clip = null;
                return clip?.ToArray() ?? Array.Empty<float>();
            }
        }

        /// <summary>True if Silero finds speech in at least one window (always true without Silero).</summary>
        public bool ContainsSpeech(float[] audio)
        {
            if (_silero == null) return true;
            lock (_sileroLock)
            {
                _silero.ResetState();
                var frame = new float[SILERO_WINDOW];
                for (int i = 0; i <= audio.Length - SILERO_WINDOW; i += SILERO_WINDOW)
                {
                    Array.Copy(audio, i, frame, 0, SILERO_WINDOW);
                    if (_silero.Evaluate(frame) >= SILERO_THRESHOLD)
                        return true;
                }
                return false;
            }
        }

        public void Dispose()
        {
            _capture.Dispose();
            lock (_sileroLock)
                _silero?.Dispose();
        }
    }
}
