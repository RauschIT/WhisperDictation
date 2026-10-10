// Copyright (c) 2026 Rausch IT

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WhisperDictation.Core.Services
{
    public class HotkeyService : IDisposable
    {
        private const uint WM_HOTKEY = 0x0312;
        private const uint WM_QUIT = 0x0012;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_SPACE = 0x20;
        private const uint VK_CONTROL = 0x11;
        private const int CTRL_POLL_INTERVAL_MS = 15;
        private const long DOUBLE_TAP_WINDOW_MS = 350;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

        public event Action? OnToggle;

        private Thread? _thread;
        private volatile bool _running;
        private uint _threadId;
        private string _mode = "CtrlSpace";
        private long _lastCtrlTapMs;

        public void Start(string mode)
        {
            if (_running)
                return;

            _mode = NormalizeMode(mode);
            _running = true;
            _thread = new Thread(HotkeyLoop) { IsBackground = true, Name = "Hotkey" };
            _thread.Start();
        }

        private static string NormalizeMode(string? mode)
        {
            if (string.Equals(mode, "CtrlDoubleTap", StringComparison.OrdinalIgnoreCase))
                return "CtrlDoubleTap";
            return "CtrlSpace";
        }

        private void HotkeyLoop()
        {
            _threadId = GetCurrentThreadId();

            if (_mode == "CtrlDoubleTap")
                RunCtrlDoubleTapLoop();
            else
                RunCtrlSpaceLoop();

            _threadId = 0;
        }

        private void RunCtrlSpaceLoop()
        {
            if (!RegisterHotKey(IntPtr.Zero, 1, MOD_CONTROL | MOD_NOREPEAT, VK_SPACE))
                return;

            while (_running)
            {
                if (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_HOTKEY)
                        OnToggle?.Invoke();
                    else if (msg.message == WM_QUIT)
                        break;
                }
            }

            UnregisterHotKey(IntPtr.Zero, 1);
        }

        private void RunCtrlDoubleTapLoop()
        {
            _lastCtrlTapMs = 0;
            bool wasDown = false;

            while (_running)
            {
                bool isDown = (GetAsyncKeyState((int)VK_CONTROL) & 0x8000) != 0;

                if (isDown && !wasDown)
                {
                    long now = Environment.TickCount64;
                    if (now - _lastCtrlTapMs <= DOUBLE_TAP_WINDOW_MS)
                    {
                        _lastCtrlTapMs = 0;
                        OnToggle?.Invoke();
                    }
                    else
                    {
                        _lastCtrlTapMs = now;
                    }
                }

                wasDown = isDown;
                Thread.Sleep(CTRL_POLL_INTERVAL_MS);
            }
        }

        public void Dispose()
        {
            if (!_running)
                return;

            _running = false;
            if (_threadId != 0)
                PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            _thread?.Join(500);
        }
    }
}
