using System;
using System.Runtime.InteropServices;

namespace ExamLock
{
    internal static class NativeMethods
    {
        public const int  WH_KEYBOARD_LL       = 13;
        public const int  WM_KEYDOWN            = 0x0100;
        public const int  WM_KEYUP              = 0x0101;
        public const int  WM_SYSKEYDOWN         = 0x0104;
        public const int  WM_SYSKEYUP           = 0x0105;
        public const uint ES_CONTINUOUS         = 0x80000000;
        public const uint ES_DISPLAY_REQUIRED   = 0x00000002;
        public const uint ES_SYSTEM_REQUIRED    = 0x00000001;
        public const int  SW_HIDE               = 0;
        public const int  SW_SHOW               = 5;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode;
            public uint   scanCode;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}
