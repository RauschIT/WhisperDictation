// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Audio capture through NAudio — 16kHz, 16bit, mono directly from the microphone.
    /// </summary>
    public class NAudioCapture : IAudioCapture
    {
        private WaveInEvent? _waveIn;
        private Action<float[]>? _onData;

        public bool IsNativeFormat => true;
        public const int OUTPUT_SAMPLE_RATE = 16000;

        public void Start(Action<float[]> onData)
        {
            _onData = onData;

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100,
                DeviceNumber = 0
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            Console.WriteLine($"Microphone: NAudio 16kHz/16bit/mono");
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            // Int16 PCM -> float32 (what Whisper expects)
            int sampleCount = e.BytesRecorded / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                samples[i] = sample / 32768f;
            }

            _onData?.Invoke(samples);
        }

        public void Stop()
        {
            _waveIn?.StopRecording();
        }

        public void Dispose()
        {
            Stop();
            _waveIn?.Dispose();
        }
    }
}
