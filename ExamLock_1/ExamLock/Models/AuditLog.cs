using System;
using System.Collections.Generic;
using System.IO;

namespace ExamLock.Models
{
    public class AuditLog
    {
        private readonly List<string> _entries = new();
        private readonly object _lock = new();

        public void Add(string message)
        {
            lock (_lock)
                _entries.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }

        public void WriteToDisk(string filePath)
        {
            lock (_lock)
                File.WriteAllLines(filePath, _entries);
        }
    }
}
