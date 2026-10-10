// Copyright (c) 2026 Rausch IT

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Combines AudioCapture + WebRTC VAD + Silero VAD V5.
    /// WebRTC drives real-time segment detection (start/stop/silence).
    /// Silero validates the finished segment afterwards — discards false positives.
    /// </summary>
    public class SpeechSegmentDetector : IDisposable
    {
        private readonly WebRtcVadService _webRtcVad;
        private readonly SileroVadOnnxModel? _sileroVad;
        private readonly IAudioCapture _capture;
        private readonly List<float> _speechBuffer = new();
        private readonly List<float> _rawBuffer = new();
        private readonly Queue<float[]> _preBuffer = new();
        private readonly Queue<float[]> _pendingSpeech = new();
        private readonly object _lock = new();

        private Thread? _vadThread;
        private volatile bool _running;
        private int _vadPosition;
        private bool _inSpeech;

        private const int FRAME_SAMPLES = WebRtcVadService.FRAME_SAMPLES; // 480 (30ms)
        private const int SILERO_WINDOW = SileroVadOnnxModel.WINDOW_SIZE_16K; // 512 (32ms)
        private const int PRE_BUFFER_CHUNKS = 7; // ~210ms (7 * 30ms)
        private const float SILERO_THRESHOLD = 0.5f;

        public int MinSilenceChunks { get; set; } = 5;
        public int MinSpeechSamples { get; set; } = 4000;
        public int StartSpeechFrames { get; set; } = 24;

        /// <summary>
        /// Minimum share of Silero windows (0..1) that must be detected as speech
        /// for a segment to be accepted. 0 = previous behaviour (a single window is enough).
        /// Higher values reject noise-triggered false segments more strictly.
        /// </summary>
        public double SileroSpeechRatio { get; set; } = 0.0;

        public event Action<float[]>? OnSpeechSegment;
        public string? DebugAudioPath { get; set; }
        private List<float>? _debugFullStream;

        public SpeechSegmentDetector(int vadMode = 3, IAudioCapture? capture = null, string? sileroModelPath = null)
        {
            _webRtcVad = new WebRtcVadService();
            _webRtcVad.Initialize((WebRtcVadSharp.OperatingMode)vadMode);

            if (!string.IsNullOrEmpty(sileroModelPath) && File.Exists(sileroModelPath))
            {
                _sileroVad = new SileroVadOnnxModel(sileroModelPath);
            }

            _capture = capture ?? new NAudioCapture();
        }

        public void Start()
        {
            _running = true;
            lock (_lock)
            {
                _rawBuffer.Clear();
                _speechBuffer.Clear();
                _pendingSpeech.Clear();
                _vadPosition = 0;
                _inSpeech = false;
            }

            if (!string.IsNullOrEmpty(DebugAudioPath))
                _debugFullStream = new List<float>();

            _capture.Start(OnAudioData);
            _vadThread = new Thread(VadLoop) { IsBackground = true, Name = "VAD" };
            _vadThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _capture.Stop();
            _vadThread?.Join(2000);
            // Emit the last segment if it contains speech
            lock (_lock)
            {
                if (_speechBuffer.Count >= MinSpeechSamples)
                    OnSpeechSegment?.Invoke(_speechBuffer.ToArray());
                _speechBuffer.Clear();

                // Debug: kompletten Stream speichern
                if (_debugFullStream != null && _debugFullStream.Count > 0 && !string.IsNullOrEmpty(DebugAudioPath))
                {
                    string path = Path.Combine(DebugAudioPath, $"full_stream_{DateTime.Now:HHmmss}.wav");
                    SaveWav(path, _debugFullStream.ToArray());
                    _debugFullStream = null;
                }
            }
        }

        private void OnAudioData(float[] samples)
        {
            lock (_lock)
            {
                _rawBuffer.AddRange(samples);
                _debugFullStream?.AddRange(samples);
            }
        }

        private void VadLoop()
        {
            int silenceCount = 0;
            int speechRunCount = 0;

            while (_running)
            {
                Thread.Sleep(10);

                // Process all available chunks, not just one
                while (true)
                {
                    float[] chunk;
                    lock (_lock)
                    {
                        if (_rawBuffer.Count - _vadPosition < FRAME_SAMPLES) break;
                        chunk = _rawBuffer.GetRange(_vadPosition, FRAME_SAMPLES).ToArray();
                    }

                    // WebRTC alone decides per frame (all settings apply here)
                    bool isSpeech = _webRtcVad.HasSpeech(chunk);

                float[]? emitSegment = null;

                lock (_lock)
                {
                    if (isSpeech)
                    {
                        if (_inSpeech)
                        {
                            _speechBuffer.AddRange(chunk);
                            silenceCount = 0;
                        }
                        else
                        {
                            speechRunCount++;
                            _pendingSpeech.Enqueue(chunk);

                            if (speechRunCount >= Math.Max(1, StartSpeechFrames))
                            {
                                _inSpeech = true;
                                _speechBuffer.Clear();
                                foreach (var preChunk in _preBuffer)
                                    _speechBuffer.AddRange(preChunk);
                                _preBuffer.Clear();

                                while (_pendingSpeech.Count > 0)
                                    _speechBuffer.AddRange(_pendingSpeech.Dequeue());

                                silenceCount = 0;
                                speechRunCount = 0;
                            }
                        }
                    }
                    else if (_inSpeech)
                    {
                        silenceCount++;
                        if (silenceCount >= MinSilenceChunks)
                        {
                            if (_speechBuffer.Count >= MinSpeechSamples)
                                emitSegment = _speechBuffer.ToArray();
                            _speechBuffer.Clear();
                            _pendingSpeech.Clear();
                            _inSpeech = false;
                            silenceCount = 0;
                            speechRunCount = 0;
                        }
                    }
                    else
                    {
                        speechRunCount = 0;
                        _pendingSpeech.Clear();
                        _preBuffer.Enqueue(chunk);
                        while (_preBuffer.Count > PRE_BUFFER_CHUNKS)
                            _preBuffer.Dequeue();
                    }

                    _vadPosition += FRAME_SAMPLES;

                    if (_vadPosition > 160000)
                    {
                        _rawBuffer.RemoveRange(0, _vadPosition);
                        _vadPosition = 0;
                    }
                } // lock

                // Validate the segment with Silero, then emit it
                if (emitSegment != null)
                {
                    var seg = emitSegment;
                    if (SileroConfirms(seg))
                        ThreadPool.QueueUserWorkItem(_ => OnSpeechSegment?.Invoke(seg));
                }

                } // inner while (all chunks)
            } // outer while (_running)
        }

        private void SaveWav(string path, float[] audio)
        {
            try
            {
                using var fs = File.Create(path);
                using var bw = new BinaryWriter(fs);
                var pcm = new byte[audio.Length * 2];
                for (int i = 0; i < audio.Length; i++)
                {
                    short s = (short)(Math.Clamp(audio[i], -1f, 1f) * 32767);
                    pcm[i * 2] = (byte)(s & 0xFF);
                    pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + pcm.Length);
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
                bw.Write(pcm.Length);
                bw.Write(pcm);
            }
            catch { }
        }

        /// <summary>
        /// Runs Silero across the whole segment.
        /// Returns true if at least one window detects speech.
        /// Without Silero it always returns true.
        /// </summary>
        private bool SileroConfirms(float[] segment)
        {
            if (_sileroVad == null) return true;

            _sileroVad.ResetState();
            var frame = new float[SILERO_WINDOW];

            int total = 0;
            int speech = 0;
            for (int i = 0; i <= segment.Length - SILERO_WINDOW; i += SILERO_WINDOW)
            {
                Array.Copy(segment, i, frame, 0, SILERO_WINDOW);
                total++;
                if (_sileroVad.Evaluate(frame) >= SILERO_THRESHOLD) speech++;
            }

            if (total == 0) return true;
            if (SileroSpeechRatio <= 0.0) return speech >= 1;
            return (double)speech / total >= SileroSpeechRatio;
        }

        public void Dispose()
        {
            Stop();
            _webRtcVad.Dispose();
            _sileroVad?.Dispose();
            _capture.Dispose();
        }
    }
}
