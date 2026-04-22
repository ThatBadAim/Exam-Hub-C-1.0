using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using ExamLock.Models;

namespace ExamLock.Services
{
    public class LockdownService : IDisposable
    {
        private IntPtr _hookHandle = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _hookProc; // keep alive — prevents GC
        private Thread? _monitorThread;
        private volatile bool _monitorRunning;
        private readonly AuditLog _auditLog;
        private IntPtr _taskbarHandle = IntPtr.Zero;

        // Sticky Keys tracking
        private int      _shiftCount;
        private DateTime _lastShift = DateTime.MinValue;

        // Filter Keys tracking
        private bool     _rShiftHeld;
        private DateTime _rShiftStart;

        private static readonly HashSet<string> BlockedProcesses =
            new(StringComparer.OrdinalIgnoreCase)
        {
            "taskmgr","cmd","powershell","pwsh","regedit","mmc","msconfig","control",
            "devmgmt","compmgmt","services","gpedit",
            "notepad","wordpad","write",
            "SnippingTool","ScreenSketch","ScreenClippingHost",
            "GameBar","GameBarFTServer","XboxGameBarWidgets",
            "msedge","chrome","firefox","opera","brave","iexplore","vivaldi","arc",
            "wt","wsl","wslhost","mstsc",
            "TeamViewer","AnyDesk","rustdesk",
            "OneDrive","SearchHost","SearchIndexer","Cortana","SystemSettings",
            "utilman","magnify","narrator"
        };

        public LockdownService(AuditLog auditLog) => _auditLog = auditLog;

        // ── Keyboard hook ─────────────────────────────────────────────────────

        public void InstallKeyboardHook()
        {
            _hookProc  = HookCallback;
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);
        }

        public void RemoveKeyboardHook()
        {
            if (_hookHandle == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var kb    = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            uint vk   = kb.vkCode;
            int  msg  = wParam.ToInt32();
            bool down = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
            bool up   = msg == NativeMethods.WM_KEYUP   || msg == NativeMethods.WM_SYSKEYUP;

            bool alt   = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool ctrl  = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool win   = (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                         (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0;

            // Win key and all Win combos
            if (vk == 0x5B || vk == 0x5C) return new IntPtr(1);
            if (win)                        return new IntPtr(1);

            // Alt combos: F4, Tab, F, Space
            if (alt && (vk == 0x73 || vk == 0x09 || vk == 0x46 || vk == 0x20))
                return new IntPtr(1);

            // Ctrl+Shift+Esc, Ctrl+Esc
            if (ctrl && shift && vk == 0x1B) return new IntPtr(1);
            if (ctrl && vk == 0x1B)          return new IntPtr(1);

            // Ctrl+W, Ctrl+N
            if (ctrl && (vk == 0x57 || vk == 0x4E)) return new IntPtr(1);

            // PrintScreen
            if (vk == 0x2C) return new IntPtr(1);

            // F1, F3, F10, F11, F12
            if (vk == 0x70 || vk == 0x72 || vk == 0x79 || vk == 0x7A || vk == 0x7B)
                return new IntPtr(1);

            // Sticky Keys: Shift ×5
            if ((vk == 0x10 || vk == 0xA0 || vk == 0xA1) && down)
            {
                var now = DateTime.UtcNow;
                _shiftCount = (now - _lastShift).TotalMilliseconds <= 500 ? _shiftCount + 1 : 1;
                _lastShift  = now;
                if (_shiftCount >= 5) { _shiftCount = 0; return new IntPtr(1); }
            }

            // Filter Keys: Right Shift held 8 s
            if (vk == 0xA1)
            {
                if (down && !_rShiftHeld) { _rShiftHeld = true; _rShiftStart = DateTime.UtcNow; }
                else if (up)             { _rShiftHeld = false; }
                if (_rShiftHeld && (DateTime.UtcNow - _rShiftStart).TotalSeconds >= 8)
                    return new IntPtr(1);
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ── Process monitor ───────────────────────────────────────────────────

        public void StartProcessMonitor()
        {
            _monitorRunning = true;
            _monitorThread  = new Thread(MonitorLoop)
            {
                IsBackground = false,
                Name         = "ExamProcessMonitor"
            };
            _monitorThread.Start();
        }

        public void StopProcessMonitor() => _monitorRunning = false;

        private void MonitorLoop()
        {
            while (_monitorRunning)
            {
                try
                {
                    foreach (var proc in Process.GetProcesses())
                    {
                        try
                        {
                            string name = proc.ProcessName;
                            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                            {
                                // Only kill File Explorer windows, not the shell
                                if (!string.IsNullOrEmpty(proc.MainWindowTitle))
                                {
                                    proc.Kill();
                                    _auditLog.Add($"Killed File Explorer window: \"{proc.MainWindowTitle}\"");
                                }
                            }
                            else if (BlockedProcesses.Contains(name))
                            {
                                proc.Kill();
                                _auditLog.Add($"Killed blocked process: {name}");
                            }
                        }
                        catch { }
                        finally { proc.Dispose(); }
                    }
                }
                catch { }
                Thread.Sleep(2000);
            }
        }

        // ── Taskbar ───────────────────────────────────────────────────────────

        public void HideTaskbar()
        {
            _taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (_taskbarHandle != IntPtr.Zero)
                NativeMethods.ShowWindow(_taskbarHandle, NativeMethods.SW_HIDE);
        }

        public void ShowTaskbar()
        {
            if (_taskbarHandle != IntPtr.Zero)
                NativeMethods.ShowWindow(_taskbarHandle, NativeMethods.SW_SHOW);
        }

        // ── Power / sleep prevention ──────────────────────────────────────────

        public void PreventSleep() =>
            NativeMethods.SetThreadExecutionState(
                NativeMethods.ES_CONTINUOUS |
                NativeMethods.ES_DISPLAY_REQUIRED |
                NativeMethods.ES_SYSTEM_REQUIRED);

        public void RestoreSleep() =>
            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);

        // ── Xbox GameDVR ──────────────────────────────────────────────────────

        public void DisableGameDVR()
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                k?.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        public void RestoreGameDVR()
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                k?.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        // ── Screen capture block ──────────────────────────────────────────────

        public static void SetScreenCaptureBlock(IntPtr hwnd) =>
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            RemoveKeyboardHook();
            StopProcessMonitor();
        }
    }
}
