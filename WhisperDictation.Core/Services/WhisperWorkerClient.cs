// Copyright (c) 2026 Rausch IT

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Runs Whisper in a worker process (<see cref="WhisperWorker"/>) with the same calls as
    /// <see cref="WhisperService"/>. Dispose ends the process: only that frees the model and the
    /// Vulkan GPU device completely, so a switch from GPU to CPU leaves no graphics memory behind.
    /// </summary>
    public class WhisperWorkerClient : IDisposable
    {
        private const string WorkerExe = "WhisperDictation.Core.exe";
        private const int BufferSize = 64 * 1024;
        private const int ExitTimeoutMs = 3000;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        private Process? _process;
        private NamedPipeServerStream? _pipe;
        private BinaryReader? _reader;
        private BinaryWriter? _writer;
        private volatile bool _loaded;
        // One answered request at a time; _writeLock also covers the unanswered setters.
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly object _writeLock = new();

        public bool IsReady => _loaded && _process is { HasExited: false };

        /// <summary>Process id of the worker, 0 if none is running.</summary>
        public int ProcessId => _process?.Id ?? 0;

        public void Initialize(string modelPath, string language = "de", bool useGpu = true)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Whisper model not found.", modelPath);
            string exe = Path.Combine(AppContext.BaseDirectory, WorkerExe);
            if (!File.Exists(exe))
                throw new FileNotFoundException("Whisper worker not found.", exe);

            // Random name, current user only. The handle is not inherited, so the worker sees the
            // end of the pipe as soon as this process closes it or exits.
            string pipeName = $"WhisperDictation-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, BufferSize, BufferSize);
            var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
            _pipe = pipe;
            _reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
            _writer = writer;

            var startInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add(WhisperWorker.Argument);
            startInfo.ArgumentList.Add(pipeName);

            try
            {
                var process = Process.Start(startInfo)!;
                _process = process;
                WaitForConnection(pipe, process);

                writer.Write(WhisperWorker.Load);
                writer.Write(modelPath);
                writer.Write(language);
                writer.Write(useGpu);
                writer.Flush();
                ReadAnswer();
                _loaded = true;
            }
            catch
            {
                Stop();
                throw;
            }
        }

        private static void WaitForConnection(NamedPipeServerStream pipe, Process process)
        {
            using var cts = new CancellationTokenSource(StartTimeout);
            var connect = pipe.WaitForConnectionAsync(cts.Token);
            Task.WaitAny(connect, process.WaitForExitAsync(cts.Token));
            cts.Cancel();
            if (!connect.IsCompletedSuccessfully)
                throw new IOException(process.HasExited
                    ? $"The Whisper worker exited on start (code {process.ExitCode})."
                    : "The Whisper worker did not connect.");
        }

        public void SetHotwords(string hotwords) => Send(WhisperWorker.SetHotwords, hotwords ?? "");

        public void SetLanguage(string language) => Send(WhisperWorker.SetLanguage, language ?? "");

        // Not answered, so a running transcription does not block the caller (UI thread);
        // the worker applies the value after it.
        private void Send(byte message, string value)
        {
            lock (_writeLock)
            {
                if (_writer == null) return;
                try
                {
                    _writer.Write(message);
                    _writer.Write(value);
                    _writer.Flush();
                }
                catch (IOException)
                {
                    // The worker has exited; IsReady reports it.
                }
            }
        }

        public async Task<string?> TranscribeAsync(float[] audio, string? prompt = null)
        {
            if (!IsReady || audio.Length == 0) return null;

            await _lock.WaitAsync();
            try
            {
                // Disposed while waiting for the lock (model switch).
                var writer = _writer;
                if (!IsReady || writer == null) return null;

                // The answer comes once the worker is done; wait for it off the calling thread.
                return await Task.Run(() =>
                {
                    lock (_writeLock)
                    {
                        writer.Write(WhisperWorker.Transcribe);
                        writer.Write(prompt ?? "");
                        writer.Write(audio.Length);
                        writer.Write(MemoryMarshal.AsBytes(audio.AsSpan()));
                        writer.Flush();
                    }
                    return ReadAnswer();
                });
            }
            finally
            {
                _lock.Release();
            }
        }

        // The worker's result, or its error message as an exception.
        private string ReadAnswer()
        {
            var reader = _reader ?? throw new ObjectDisposedException(nameof(WhisperWorkerClient));
            bool ok;
            string text;
            try
            {
                ok = reader.ReadBoolean();
                text = reader.ReadString();
            }
            catch (EndOfStreamException ex)
            {
                throw new IOException("The Whisper worker has exited.", ex);
            }
            return ok ? text : throw new InvalidOperationException(text);
        }

        // Waits for a running transcription, so the worker can be replaced while dictating
        // (model or CPU/GPU switch). Later TranscribeAsync calls return null.
        public void Dispose()
        {
            _lock.Wait();
            try
            {
                Stop();
            }
            finally
            {
                _lock.Release();
            }
        }

        // Closing the pipe makes the worker unload the model and exit; kill it if it does not.
        // Either way the process ends, and Windows frees all of its RAM and GPU memory.
        private void Stop()
        {
            _loaded = false;
            lock (_writeLock)
            {
                _pipe?.Dispose();
                _pipe = null;
                _reader = null;
                _writer = null;
            }

            var process = _process;
            _process = null;
            if (process == null) return;
            if (!process.WaitForExit(ExitTimeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Exited in the meantime.
                }
                process.WaitForExit();
            }
            process.Dispose();
        }
    }
}
