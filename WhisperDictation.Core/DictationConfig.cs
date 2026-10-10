// Copyright (c) 2026 Rausch IT

namespace WhisperDictation.Core
{
    public class DictationConfig
    {
        public int VadMode { get; set; } = 3;  // 0=Quality, 1=LowBitrate, 2=Aggressive, 3=VeryAggressive
        public int MinSilenceMs { get; set; } = 160;
        public int MinSpeechMs { get; set; } = 700;
        public int StartSpeechFrames { get; set; } = 24;
        public int WordDelayMs { get; set; } = 60;
        public string HotkeyMode { get; set; } = "CtrlSpace";
        public string Hotwords { get; set; } = "";
        public string WhisperModel { get; set; } = "ggml-large-v3.bin";
        public bool UseGpu { get; set; } = true;  // false = run Whisper on the CPU
        public string Language { get; set; } = "de";
    }
}
