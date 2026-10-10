using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WhisperDictation.Core.Services
{
    /// <summary>
    /// Silero VAD V5 ONNX inference with correct context handling.
    /// Based on VadSharp by Gabriele Bologna (MIT, https://github.com/GabryB03/VadSharp),
    /// adapted for real-time single frame evaluation.
    /// See THIRD-PARTY-NOTICES.md. This file carries third-party copyright and
    /// deliberately has no Rausch IT copyright header.
    /// </summary>
    internal class SileroVadOnnxModel : IDisposable
    {
        private readonly InferenceSession _session;
        private float[][][] _state;
        private float[][] _context;
        private int _lastSr, _lastBatchSize;

        public const int WINDOW_SIZE_16K = 512;  // 32ms at 16kHz
        private const int CONTEXT_SIZE_16K = 64;

        public SileroVadOnnxModel(string modelPath)
        {
            var options = new SessionOptions();
            options.InterOpNumThreads = 1;
            options.IntraOpNumThreads = 1;
            options.EnableCpuMemArena = true;
            _session = new InferenceSession(modelPath, options);

            _state = CreateEmptyState();
            _context = Array.Empty<float[]>();
        }

        public void ResetState()
        {
            _state = CreateEmptyState();
            _context = Array.Empty<float[]>();
            _lastSr = 0;
            _lastBatchSize = 0;
        }

        private static float[][][] CreateEmptyState()
        {
            return new float[2][][]
            {
                new float[1][] { new float[128] },
                new float[1][] { new float[128] }
            };
        }

        /// <summary>
        /// Computes the speech probability for a single 512-sample frame (16kHz).
        /// Returns a value between 0.0 and 1.0.
        /// </summary>
        public float Evaluate(float[] frame, int sampleRate = 16000)
        {
            if (frame.Length != WINDOW_SIZE_16K)
                throw new ArgumentException($"Frame must be {WINDOW_SIZE_16K} samples, got {frame.Length}");

            float[][] x = new float[][] { frame };
            int batchSize = 1;

            // Reset state/context when something changes
            if (_lastBatchSize == 0 || _lastSr != sampleRate || _lastBatchSize != batchSize)
                ResetState();

            // Prepare the context (last 64 samples of the previous frame)
            if (_context.Length != batchSize)
            {
                _context = new float[batchSize][];
                for (int i = 0; i < batchSize; i++)
                    _context[i] = new float[CONTEXT_SIZE_16K];
            }

            // Context + Frame zusammenfuegen
            float[][] xWithContext = Concatenate(_context, x);
            int cols = xWithContext[0].Length;
            float[] inputData = new float[cols];
            Array.Copy(xWithContext[0], inputData, cols);

            var inputTensor = new DenseTensor<float>(inputData, new[] { 1, cols });
            var srTensor = new DenseTensor<long>(new long[] { sampleRate }, new[] { 1 });

            // State zu flachem Array
            float[] stateData = new float[2 * 1 * 128];
            int idx = 0;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 1; j++)
                {
                    Array.Copy(_state[i][j], 0, stateData, idx, 128);
                    idx += 128;
                }

            var stateTensor = new DenseTensor<float>(stateData, new[] { 2, 1, 128 });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("sr", srTensor),
                NamedOnnxValue.CreateFromTensor("state", stateTensor)
            };

            using var outputs = _session.Run(inputs);
            var outputTensor = outputs.First(o => o.Name == "output").AsTensor<float>();
            var newStateTensor = outputs.First(o => o.Name == "stateN").AsTensor<float>();

            // Update the context (last 64 samples of the input frame)
            _context = GetLastColumns(xWithContext, CONTEXT_SIZE_16K);
            _lastSr = sampleRate;
            _lastBatchSize = batchSize;

            // Update the state
            var dims = newStateTensor.Dimensions;
            float[] newStateFlat = newStateTensor.ToArray();
            idx = 0;
            for (int i = 0; i < dims[0]; i++)
            {
                for (int j = 0; j < dims[1]; j++)
                {
                    Array.Copy(newStateFlat, idx, _state[i][j], 0, dims[2]);
                    idx += dims[2];
                }
            }

            return outputTensor[0];
        }

        private static float[][] Concatenate(float[][] a, float[][] b)
        {
            int rows = a.Length;
            float[][] result = new float[rows][];
            for (int i = 0; i < rows; i++)
            {
                int lenA = a[i].Length;
                int lenB = b[i].Length;
                float[] row = new float[lenA + lenB];
                Buffer.BlockCopy(a[i], 0, row, 0, lenA * sizeof(float));
                Buffer.BlockCopy(b[i], 0, row, lenA * sizeof(float), lenB * sizeof(float));
                result[i] = row;
            }
            return result;
        }

        private static float[][] GetLastColumns(float[][] array, int count)
        {
            int rows = array.Length;
            int cols = array[0].Length;
            float[][] result = new float[rows][];
            for (int i = 0; i < rows; i++)
            {
                float[] row = new float[count];
                Buffer.BlockCopy(array[i], (cols - count) * sizeof(float), row, 0, count * sizeof(float));
                result[i] = row;
            }
            return result;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
