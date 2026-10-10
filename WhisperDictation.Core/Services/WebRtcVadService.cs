// Copyright (c) 2026 Rausch IT

using System;
using WebRtcVadSharp;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// WebRTC VAD — GMM based, no machine learning, no state that can corrupt.
    /// Takes 16kHz, 16bit PCM. Frame size: 10ms, 20ms or 30ms.
    /// </summary>
    public class WebRtcVadService : IDisposable
    {
        private WebRtcVad? _vad;
        private const int SAMPLE_RATE = 16000;
        private const int FRAME_MS = 30; // 30ms frames
        public const int FRAME_SAMPLES = SAMPLE_RATE * FRAME_MS / 1000; // 480

        public void Initialize(OperatingMode mode = OperatingMode.HighQuality)
        {
            _vad = new WebRtcVad
            {
                OperatingMode = mode,
                FrameLength = FrameLength.Is30ms,
                SampleRate = SampleRate.Is16kHz
            };
        }

        /// <summary>
        /// Checks whether an audio frame contains speech.
        /// The frame must be exactly FRAME_SAMPLES float32 samples.
        /// </summary>
        public bool HasSpeech(float[] frame)
        {
            if (_vad == null || frame.Length != FRAME_SAMPLES) return false;

            // Float32 -> Int16 PCM (WebRTC erwartet byte[])
            var pcm = new byte[frame.Length * 2];
            for (int i = 0; i < frame.Length; i++)
            {
                short s = (short)(Math.Clamp(frame[i], -1f, 1f) * 32767);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }

            return _vad.HasSpeech(pcm);
        }

        public void Dispose()
        {
            _vad?.Dispose();
        }
    }
}
