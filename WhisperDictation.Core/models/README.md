# Models

## Whisper (Speech-to-Text)

Modelle im GGML-Format (Georgi Gerganov Machine Learning) — das native Tensorformat von whisper.cpp, quantisiert und optimiert fuer CPU/GPU-Inferenz.

- **ggml-large-v3.bin** (2.9 GB) — Beste Qualität, braucht GPU
  Download: https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin

- **ggml-small.bin** (466 MB) — Schneller, weniger Qualität
  Download: https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin

Weitere Modelle: https://huggingface.co/ggerganov/whisper.cpp

## Silero VAD (Voice Activity Detection)

Silero VAD ist ein vortrainiertes neuronales Netz von Silero (snakers4) zur Erkennung von Sprache vs. Stille in Audio-Streams. Es ist deutlich robuster als einfache Energy-basierte Ansaetze und unterscheidet zuverlaessig zwischen Sprache, Hintergrundgeraeuschen und Stille.

Das Modell im ONNX-Format (Open Neural Network Exchange) laeuft ueber Microsoft ONNX Runtime auf CPU — kein GPU noetig, Inferenz dauert <1ms pro 32ms Audio-Chunk.

Eingabe: 512 Samples bei 16kHz (32ms), float32, mono.
Ausgabe: Probability 0.0-1.0 (Sprache vs. Stille).
Interner State (h/c) wird zwischen Aufrufen beibehalten fuer zeitliche Kohaerenz.

- **silero_vad.onnx** (2.2 MB)
  Download: https://huggingface.co/onnx-community/silero-vad/resolve/main/onnx/model.onnx
  GitHub: https://github.com/snakers4/silero-vad

## Verwendung

Die App sucht die Modelle in diesem Verzeichnis. Whisper.net laedt das Whisper-Modell ueber Vulkan (GPU) mit CPU-Fallback. Silero VAD laeuft ueber ONNX Runtime auf CPU.

Beim Start der App wird 1 Sekunde Stille aufgenommen um den Noise-Floor des Mikrofons zu kalibrieren.
