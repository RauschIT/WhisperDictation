// Copyright (c) 2026 Rausch IT

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// WASAPI Audio-Capture im Shared Mode.
    /// Akzeptiert jedes Mikrofon-Format, konvertiert intern zu 16kHz mono float32.
    /// </summary>
    public class AudioCapture : IDisposable
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IAudioClient client);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity,
                          IntPtr format, IntPtr audioSessionGuid);
            int GetBufferSize(out uint bufferSize);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out uint padding);
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            int GetMixFormat(out IntPtr format);
            int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr eventHandle);
            int GetService(ref Guid iid, out IntPtr service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr data, out uint numFrames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            int ReleaseBuffer(uint numFrames);
            int GetNextPacketSize(out uint numFrames);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        private const int WAVE_FORMAT_PCM = 1;
        private const int WAVE_FORMAT_IEEE_FLOAT = 3;
        private const int WAVE_FORMAT_EXTENSIBLE = unchecked((short)0xFFFE);

        private IAudioClient? _client;
        private IAudioCaptureClient? _captureClient;
        private Thread? _captureThread;
        private volatile bool _capturing;
        private Action<float[]>? _onData;

        private int _deviceChannels;
        private int _deviceSampleRate;
        private int _deviceBitsPerSample;
        private int _deviceFormatTag;
        private int _bytesPerFrame;

        public const int OUTPUT_SAMPLE_RATE = 16000;

        public void Start(Action<float[]> onData)
        {
            _onData = onData;

            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            int hr = enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, 0 /* eConsole */, out var device);
            if (hr < 0)
                throw new Exception("No microphone found.");

            var audioClientGuid = typeof(IAudioClient).GUID;
            device.Activate(ref audioClientGuid, 0x17, IntPtr.Zero, out _client);

            // Query the mix format — the format WASAPI shared mode supports
            _client.GetMixFormat(out var formatPtr);
            var format = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);

            _deviceChannels = format.nChannels;
            _deviceSampleRate = format.nSamplesPerSec;
            _deviceBitsPerSample = format.wBitsPerSample;
            _deviceFormatTag = format.wFormatTag;
            _bytesPerFrame = format.nBlockAlign;

            // WAVEFORMATEXTENSIBLE: the real format tag lives in the SubFormat GUID
            if ((format.wFormatTag == unchecked((short)0xFFFE)) && format.cbSize >= 22)
            {
                // WaveFormatEx is 18 bytes: 2+2+4+4+2+2+2
                // The extensible fields follow:
                //   +18: WORD wValidBitsPerSample (2)
                //   +20: WORD wSamplesPerBlock (2)  — actually a union
                //   +22: DWORD dwChannelMask (4)
                //   +26: GUID SubFormat (16) — first 2 bytes = format tag
                // Marshal.SizeOf might add padding, so do not rely on it.
                // Safe approach: read all bytes from formatPtr and parse manually.
                int marshalSize = Marshal.SizeOf<WaveFormatEx>();
                // Extra data starts after the cbSize field.
                // cbSize states how many extra bytes follow (at least 22 for extensible).
                // The SubFormat GUID is at offset: marshalSize + 4 (wValidBits + padding) + 4 (channelMask)
                IntPtr subFormatPtr = formatPtr + marshalSize + 8;
                short realTag = Marshal.ReadInt16(subFormatPtr);
                _deviceFormatTag = realTag;

                // Debug output
                Console.WriteLine($"  -> WAVEFORMATEXTENSIBLE: marshalSize={marshalSize}, cbSize={format.cbSize}, realTag={realTag} (0x{realTag:X4})");

                // Fallback: if realTag is still 0 it is most likely IEEE_FLOAT,
                // because 48kHz/32bit/float is the usual mix format.
                if (realTag == 0 && _deviceBitsPerSample == 32)
                {
                    _deviceFormatTag = WAVE_FORMAT_IEEE_FLOAT;
                    Console.WriteLine($"  -> fallback: assuming IEEE_FLOAT");
                }
            }

            Console.WriteLine($"Microphone: {_deviceSampleRate}Hz, {_deviceChannels}ch, {_deviceBitsPerSample}bit, format={_deviceFormatTag}");

            // Initialise with the native format
            long bufferDuration = 2000000; // 200ms in 100ns
            hr = _client.Initialize(0 /* SHARED */, 0, bufferDuration, 0, formatPtr, IntPtr.Zero);
            Marshal.FreeCoTaskMem(formatPtr);

            if (hr < 0)
                throw new Exception($"WASAPI Initialize failed: 0x{hr:X8}");

            var captureGuid = typeof(IAudioCaptureClient).GUID;
            _client.GetService(ref captureGuid, out var capturePtr);
#pragma warning disable CA1416
            _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
#pragma warning restore CA1416

            _capturing = true;
            _client.Start();

            _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "AudioCapture" };
            _captureThread.Start();
        }

        private void CaptureLoop()
        {
            while (_capturing)
            {
                Thread.Sleep(20);
                try
                {
                    DrainBuffer();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AudioCapture] Error: {ex.Message}");
                }
            }
        }

        private void DrainBuffer()
        {
            _captureClient!.GetNextPacketSize(out var packetSize);
            while (packetSize > 0)
            {
                int hr = _captureClient.GetBuffer(out var dataPtr, out var numFrames, out var flags, out _, out _);
                if (hr < 0 || numFrames == 0)
                {
                    _captureClient.ReleaseBuffer(numFrames);
                    break;
                }

                // Silent-Flag: Mikrofon liefert Stille (z.B. Mute)
                bool isSilent = (flags & 0x2) != 0;

                float[] mono;
                if (isSilent)
                {
                    int outSamples = (int)(numFrames * (double)OUTPUT_SAMPLE_RATE / _deviceSampleRate);
                    mono = new float[Math.Max(1, outSamples)];
                }
                else
                {
                    mono = ConvertToMono16k(dataPtr, (int)numFrames);
                }

                _captureClient.ReleaseBuffer(numFrames);
                if (mono.Length > 0)
                    _onData?.Invoke(mono);

                _captureClient.GetNextPacketSize(out packetSize);
            }
        }

        private float[] ConvertToMono16k(IntPtr dataPtr, int numFrames)
        {
            // Step 1: raw data to float32 mono
            var mono = new float[numFrames];

            if (_deviceFormatTag == WAVE_FORMAT_IEEE_FLOAT && _deviceBitsPerSample == 32)
            {
                var raw = new float[numFrames * _deviceChannels];
                Marshal.Copy(dataPtr, raw, 0, raw.Length);
                for (int i = 0; i < numFrames; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < _deviceChannels; ch++)
                        sum += raw[i * _deviceChannels + ch];
                    mono[i] = sum / _deviceChannels;
                }
            }
            else if (_deviceFormatTag == WAVE_FORMAT_PCM && _deviceBitsPerSample == 16)
            {
                var raw = new short[numFrames * _deviceChannels];
                Marshal.Copy(dataPtr, raw, 0, raw.Length);
                for (int i = 0; i < numFrames; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < _deviceChannels; ch++)
                        sum += raw[i * _deviceChannels + ch] / 32768f;
                    mono[i] = sum / _deviceChannels;
                }
            }
            else if (_deviceFormatTag == WAVE_FORMAT_PCM && _deviceBitsPerSample == 24)
            {
                int totalSamples = numFrames * _deviceChannels;
                var bytes = new byte[totalSamples * 3];
                Marshal.Copy(dataPtr, bytes, 0, bytes.Length);
                for (int i = 0; i < numFrames; i++)
                {
                    float sum = 0;
                    for (int ch = 0; ch < _deviceChannels; ch++)
                    {
                        int idx = (i * _deviceChannels + ch) * 3;
                        int sample = bytes[idx] | (bytes[idx + 1] << 8) | ((sbyte)bytes[idx + 2] << 16);
                        sum += sample / 8388608f;
                    }
                    mono[i] = sum / _deviceChannels;
                }
            }
            else
            {
                // Unbekanntes Format: Stille
                Console.WriteLine($"[AudioCapture] Unknown format: tag={_deviceFormatTag}, bits={_deviceBitsPerSample}");
                return new float[numFrames * OUTPUT_SAMPLE_RATE / _deviceSampleRate];
            }

            // Step 2: resample to 16kHz
            if (_deviceSampleRate == OUTPUT_SAMPLE_RATE)
                return mono;

            double ratio = (double)OUTPUT_SAMPLE_RATE / _deviceSampleRate;
            int outLen = (int)(mono.Length * ratio);
            if (outLen == 0) return Array.Empty<float>();

            var resampled = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double srcIdx = i / ratio;
                int idx = (int)srcIdx;
                float frac = (float)(srcIdx - idx);
                if (idx + 1 < mono.Length)
                    resampled[i] = mono[idx] * (1 - frac) + mono[idx + 1] * frac;
                else
                    resampled[i] = mono[Math.Min(idx, mono.Length - 1)];
            }
            return resampled;
        }

        public void Stop()
        {
            _capturing = false;
            _captureThread?.Join(2000);
            try { _client?.Stop(); } catch { }
            try { _client?.Reset(); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
