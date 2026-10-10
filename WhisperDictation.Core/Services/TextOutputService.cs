// Copyright (c) 2026 Rausch IT

using System;
using System.Runtime.InteropServices;

namespace WhisperDictation.Core.Services
{
    public class TextOutputService
    {
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        public void Type(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var inputs = new INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i * 2] = new INPUT
                {
                    type = 1,
                    u = new INPUTUNION { ki = new KEYBDINPUT { wScan = (ushort)text[i], dwFlags = 0x0004 } }
                };
                inputs[i * 2 + 1] = new INPUT
                {
                    type = 1,
                    u = new INPUTUNION { ki = new KEYBDINPUT { wScan = (ushort)text[i], dwFlags = 0x0004 | 0x0002 } }
                };
            }
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
    }
}
