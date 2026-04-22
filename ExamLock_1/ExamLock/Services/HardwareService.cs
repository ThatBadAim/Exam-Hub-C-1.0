using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ExamLock.Models;

namespace ExamLock.Services
{
    public class HardwareService
    {
        private readonly List<string> _disabledInterfaces = new();
        private readonly AuditLog _auditLog;

        public HardwareService(AuditLog auditLog) => _auditLog = auditLog;

        // ── Network ───────────────────────────────────────────────────────────

        public void DisableNetworkInterfaces()
        {
            try
            {
                var names = GetInterfaceNames();
                foreach (var name in names)
                {
                    try
                    {
                        RunNetsh($"interface set interface \"{name}\" admin=disable");
                        _disabledInterfaces.Add(name);
                        _auditLog.Add($"Network interface disabled: {name}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _auditLog.Add($"Network disable error (non-fatal): {ex.Message}");
            }
        }

        public void RestoreNetworkInterfaces()
        {
            foreach (var name in _disabledInterfaces)
            {
                try
                {
                    RunNetsh($"interface set interface \"{name}\" admin=enable");
                    _auditLog.Add($"Network interface re-enabled: {name}");
                }
                catch { }
            }
            _disabledInterfaces.Clear();
        }

        private static List<string> GetInterfaceNames()
        {
            var names  = new List<string>();
            string raw = RunNetshOutput("interface show interface");

            // Output has header lines then rows like:
            // Enabled    Connected    Dedicated    Ethernet
            // Skip the first 3 header lines, grab the last token of each subsequent line
            var lines = raw.Split('\n');
            for (int i = 3; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Split on 2+ spaces to isolate the interface name column
                var parts = Regex.Split(line, @"\s{2,}");
                if (parts.Length >= 4)
                    names.Add(parts[^1].Trim());
            }
            return names;
        }

        private static void RunNetsh(string args)
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName               = "netsh",
                Arguments              = args,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = false,
                RedirectStandardError  = false
            };
            p.Start();
            p.WaitForExit(5000);
        }

        private static string RunNetshOutput(string args)
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName               = "netsh",
                Arguments              = args,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
    }
}
