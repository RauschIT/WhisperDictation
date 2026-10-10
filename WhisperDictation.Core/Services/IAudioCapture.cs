// Copyright (c) 2026 Rausch IT

using System;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Interface for audio capture.
    /// Delivers normalised float32 samples in the range [-1, 1] at 16kHz mono.
    /// </summary>
    public interface IAudioCapture : IDisposable
    {
        const int OUTPUT_SAMPLE_RATE = 16000;

        /// <summary>true if the audio is natively 16kHz/16bit (e.g. NAudio), false when resampling is needed (WASAPI)</summary>
        bool IsNativeFormat { get; }

        void Start(Action<float[]> onData);
        void Stop();
    }
}
