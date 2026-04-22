using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using ExamLock.Models;

namespace ExamLock.Services
{
    public class AutoSaveService : IDisposable
    {
        private DispatcherTimer? _timer;
        private Func<ExamSession>? _snapshotProvider;
        private Action<DateTime>?  _onSaved;

        public static string SessionFilePath =>
            Path.Combine(Path.GetTempPath(), "examlock_session.json");

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Start(Func<ExamSession> snapshotProvider, Action<DateTime> onSaved)
        {
            _snapshotProvider = snapshotProvider;
            _onSaved          = onSaved;
            _timer            = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _timer.Tick      += OnTick;
            _timer.Start();
        }

        public void Stop() => _timer?.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            if (_snapshotProvider == null) return;
            var session = _snapshotProvider(); // called on UI thread — safe to read UI state
            Task.Run(() => Write(session)).ContinueWith(t =>
            {
                if (!t.IsFaulted)
                    _onSaved?.Invoke(DateTime.Now);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ── Static helpers ────────────────────────────────────────────────────

        public static async Task SaveNowAsync(ExamSession session) =>
            await Task.Run(() => Write(session));

        private static void Write(ExamSession session)
        {
            try
            {
                session.LastSaved  = DateTime.UtcNow;
                session.InProgress = true;
                string json = JsonSerializer.Serialize(session,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SessionFilePath, json);
            }
            catch { }
        }

        public static ExamSession? TryLoadSession()
        {
            try
            {
                if (!File.Exists(SessionFilePath)) return null;
                var session = JsonSerializer.Deserialize<ExamSession>(
                    File.ReadAllText(SessionFilePath));
                return session?.InProgress == true ? session : null;
            }
            catch { return null; }
        }

        public static void DeleteSession()
        {
            try { if (File.Exists(SessionFilePath)) File.Delete(SessionFilePath); }
            catch { }
        }

        public void Dispose() => Stop();
    }
}
