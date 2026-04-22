using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ExamLock.Models;
using ExamLock.Services;

namespace ExamLock.ViewModels
{
    public class ExamViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ExamSession     _session;
        private readonly AuditLog        _auditLog;
        private readonly AutoSaveService _autoSave;
        private DispatcherTimer?         _timer;

        // ── Exit overlay state ────────────────────────────────────────────────
        private bool   _exitOverlayVisible;
        private string _exitPasswordError  = string.Empty;
        private bool   _exitInputEnabled   = true;
        private int    _cooldownSeconds;
        private int    _wrongAttempts;
        private int    _currentLockoutSeconds = 30;
        private DispatcherTimer? _cooldownTimer;

        // ── Warning banner ────────────────────────────────────────────────────
        private string _warningText     = string.Empty;
        private bool   _warningVisible;
        private string _warningColor    = "#f0c040";
        private DispatcherTimer? _bannerTimer;

        // ── Timer display ─────────────────────────────────────────────────────
        private string _timerDisplay = string.Empty;
        private string _timerColor   = "#ffffff";

        // ── Status bar ────────────────────────────────────────────────────────
        private string _lastSavedText = "Not saved yet";
        private int    _wordCount;

        // ── Recovery banner ───────────────────────────────────────────────────
        private bool   _recoveryBannerVisible;

        // ── Callbacks set by ExamWindow ───────────────────────────────────────
        public Func<string>?         GetRtfContent      { get; set; }
        public Action<string>?       SetRtfContent      { get; set; }
        public Action?               OnExamComplete     { get; set; }
        public Action<ExamSession>?  OnWriteOutput      { get; set; }

        // ── Public properties ─────────────────────────────────────────────────

        public ExamSession Session => _session;
        public AuditLog    AuditLog => _auditLog;

        public string HeaderText =>
            $"{_session.StudentFullName}  ·  {_session.ExamBoard} — {_session.ExamName}";

        public string TimerDisplay
        {
            get => _timerDisplay;
            private set { _timerDisplay = value; OnPropertyChanged(); }
        }

        public string TimerColor
        {
            get => _timerColor;
            private set { _timerColor = value; OnPropertyChanged(); }
        }

        public bool ExitOverlayVisible
        {
            get => _exitOverlayVisible;
            set { _exitOverlayVisible = value; OnPropertyChanged(); }
        }

        public string ExitPasswordError
        {
            get => _exitPasswordError;
            set { _exitPasswordError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExitError)); }
        }

        public bool HasExitError => !string.IsNullOrEmpty(_exitPasswordError);

        public bool ExitInputEnabled
        {
            get => _exitInputEnabled;
            set { _exitInputEnabled = value; OnPropertyChanged(); }
        }

        public int CooldownSeconds
        {
            get => _cooldownSeconds;
            set { _cooldownSeconds = value; OnPropertyChanged(); }
        }

        public string WarningText
        {
            get => _warningText;
            set { _warningText = value; OnPropertyChanged(); }
        }

        public bool WarningVisible
        {
            get => _warningVisible;
            set { _warningVisible = value; OnPropertyChanged(); }
        }

        public string WarningColor
        {
            get => _warningColor;
            set { _warningColor = value; OnPropertyChanged(); }
        }

        public string LastSavedText
        {
            get => _lastSavedText;
            set { _lastSavedText = value; OnPropertyChanged(); }
        }

        public int WordCount
        {
            get => _wordCount;
            set { _wordCount = value; OnPropertyChanged(); }
        }

        public bool RecoveryBannerVisible
        {
            get => _recoveryBannerVisible;
            set { _recoveryBannerVisible = value; OnPropertyChanged(); }
        }

        // ── Constructor ───────────────────────────────────────────────────────

        public ExamViewModel(ExamSession session, AuditLog auditLog, AutoSaveService autoSave)
        {
            _session  = session;
            _auditLog = auditLog;
            _autoSave = autoSave;
            UpdateTimerDisplay();
        }

        // ── Startup ───────────────────────────────────────────────────────────

        public void Start(bool isCrashRecovery)
        {
            if (isCrashRecovery)
            {
                _auditLog.Add("Crash recovery: session restored.");
                ShowRecoveryBanner();
            }
            else
            {
                _auditLog.Add($"Exam started — Board:{_session.ExamBoard}  Name:{_session.ExamName}  " +
                              $"Student:{_session.StudentFullName}  No:{_session.StudentNumber}  " +
                              $"Centre:{_session.ExamCentreNumber}  " +
                              $"Timed:{_session.IsTimedExam}  Duration:{_session.DurationSeconds}s");
            }

            if (_session.IsTimedExam)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += TimerTick;
                _timer.Start();
            }
            else
            {
                TimerDisplay = "Untimed";
                TimerColor   = "#a0a0b0";
            }

            _autoSave.Start(BuildSnapshot, saved =>
            {
                LastSavedText = $"Last saved: {saved:HH:mm:ss}";
                _auditLog.Add("Auto-save written.");
            });
        }

        // ── Timer ─────────────────────────────────────────────────────────────

        private void TimerTick(object? sender, EventArgs e)
        {
            if (_session.RemainingSeconds <= 0)
            {
                _timer?.Stop();
                TimerDisplay = "00:00:00";
                TimerColor   = "#e94560";
                TriggerTimeUp();
                return;
            }

            _session.RemainingSeconds--;
            UpdateTimerDisplay();

            if (_session.IsTimedExam)
            {
                if (_session.RemainingSeconds == 1800)
                    ShowWarning("30 minutes remaining.", "#f0c040");
                else if (_session.RemainingSeconds == 600)
                    ShowWarning("10 minutes remaining.", "#e94560");
            }
        }

        private void UpdateTimerDisplay()
        {
            if (!_session.IsTimedExam) return;
            int s = Math.Max(0, _session.RemainingSeconds);
            TimerDisplay = $"{s / 3600:D2}:{s % 3600 / 60:D2}:{s % 60:D2}";

            TimerColor = _session.RemainingSeconds <= 600 ? "#e94560"
                       : _session.RemainingSeconds <= 1800 ? "#f0c040"
                       : "#ffffff";
        }

        private void TriggerTimeUp()
        {
            _auditLog.Add("Timer reached zero — exit prompt shown.");
            ExitPasswordError  = "Time is up. Please enter the invigilator password to end the exam.";
            ExitOverlayVisible = true;
        }

        // ── Warning banner ────────────────────────────────────────────────────

        private void ShowWarning(string text, string color)
        {
            _bannerTimer?.Stop();
            WarningText    = text;
            WarningColor   = color;
            WarningVisible = true;

            _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _bannerTimer.Tick += (_, _) =>
            {
                WarningVisible = false;
                _bannerTimer?.Stop();
            };
            _bannerTimer.Start();
        }

        // ── Recovery banner ───────────────────────────────────────────────────

        private void ShowRecoveryBanner()
        {
            RecoveryBannerVisible = true;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (_, _) => { RecoveryBannerVisible = false; t.Stop(); };
            t.Start();
        }

        // ── Auto-save snapshot ────────────────────────────────────────────────

        private ExamSession BuildSnapshot()
        {
            _session.RtfContent  = GetRtfContent?.Invoke() ?? string.Empty;
            _session.InProgress  = true;
            return _session;
        }

        // ── Exit flow ─────────────────────────────────────────────────────────

        public void ShowExitOverlay()
        {
            ExitPasswordError  = string.Empty;
            ExitOverlayVisible = true;
        }

        public void CancelExitOverlay()
        {
            ExitOverlayVisible = false;
            ExitPasswordError  = string.Empty;
        }

        public void SubmitExitPassword(string password)
        {
            if (!ExitInputEnabled) return;

            if (password != _session.InvigilatorPassword)
            {
                _wrongAttempts++;
                _auditLog.Add($"Wrong invigilator password attempt #{_wrongAttempts}.");

                if (_wrongAttempts >= 3)
                {
                    int lockout = _wrongAttempts == 3
                        ? _currentLockoutSeconds
                        : (_currentLockoutSeconds = _currentLockoutSeconds * 2);

                    _auditLog.Add($"Password cooldown triggered: {lockout}s.");
                    StartCooldown(lockout);
                }
                else
                {
                    ExitPasswordError = $"Incorrect password. {3 - _wrongAttempts} attempt(s) remaining.";
                }
                return;
            }

            // Correct password
            _auditLog.Add("Exam ended by invigilator.");
            _session.InProgress = false;

            // Give window the session for file writing
            OnWriteOutput?.Invoke(_session);
        }

        private void StartCooldown(int seconds)
        {
            ExitInputEnabled  = false;
            CooldownSeconds   = seconds;
            ExitPasswordError = $"Incorrect password. Try again in {seconds}s.";

            _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cooldownTimer.Tick += (_, _) =>
            {
                CooldownSeconds--;
                ExitPasswordError = $"Incorrect password. Try again in {CooldownSeconds}s.";
                if (CooldownSeconds <= 0)
                {
                    _cooldownTimer?.Stop();
                    ExitInputEnabled  = true;
                    ExitPasswordError = "Enter the invigilator password.";
                }
            };
            _cooldownTimer.Start();
        }

        // ── Word count ────────────────────────────────────────────────────────

        public void UpdateWordCount(string plainText)
        {
            var words = plainText.Split(new[] { ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            WordCount = words.Length;
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            _timer?.Stop();
            _bannerTimer?.Stop();
            _cooldownTimer?.Stop();
            _autoSave.Stop();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
