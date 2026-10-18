// Copyright (c) 2026 Rausch IT

using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Worker side of <see cref="WhisperWorkerClient"/>: runs a <see cref="WhisperService"/> in its
    /// own process (WhisperDictation.Core.exe --whisper-worker &lt;pipe&gt;). The ggml Vulkan backend
    /// keeps its GPU device until the process ends, so freeing the model alone leaves graphics
    /// memory allocated. Ending the worker process releases all of it.
    /// </summary>
    public static class WhisperWorker
    {
        public const string Argument = "--whisper-worker";

        // Messages from the client. Load and Transcribe are answered with (bool ok, string text
        // or error message); the setters are not answered.
        internal const byte Load = 1;
        internal const byte SetLanguage = 2;
        internal const byte SetHotwords = 3;
        internal const byte Transcribe = 4;

        public static async Task RunAsync(string pipeName)
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(10_000);
            var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
            var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
            using var whisper = new WhisperService();

            try
            {
                // -1: the app closed the pipe (model or CPU/GPU switch) or has exited.
                int message;
                while ((message = pipe.ReadByte()) >= 0)
                {
                    switch (message)
                    {
                        case Load:
                        {
                            string modelPath = reader.ReadString();
                            string language = reader.ReadString();
                            bool useGpu = reader.ReadBoolean();
                            await AnswerAsync(writer, () =>
                            {
                                whisper.Initialize(modelPath, language, useGpu);
                                return Task.FromResult<string?>(null);
                            });
                            break;
                        }
                        case SetLanguage:
                            whisper.SetLanguage(reader.ReadString());
                            break;
                        case SetHotwords:
                            whisper.SetHotwords(reader.ReadString());
                            break;
                        case Transcribe:
                        {
                            string prompt = reader.ReadString();
                            var audio = new float[reader.ReadInt32()];
                            pipe.ReadExactly(MemoryMarshal.AsBytes(audio.AsSpan()));
                            await AnswerAsync(writer, () => whisper.TranscribeAsync(audio, prompt));
                            break;
                        }
                        default:
                            throw new InvalidDataException($"Unknown worker message {message}.");
                    }
                }
            }
            catch (IOException)
            {
                // The app has gone in the middle of a message; nobody is left to answer.
            }
        }

        private static async Task AnswerAsync(BinaryWriter writer, Func<Task<string?>> action)
        {
            bool ok = true;
            string text;
            try
            {
                text = await action() ?? "";
            }
            catch (Exception ex)
            {
                ok = false;
                text = ex.Message;
            }
            writer.Write(ok);
            writer.Write(text);
            writer.Flush();
        }
    }
}
