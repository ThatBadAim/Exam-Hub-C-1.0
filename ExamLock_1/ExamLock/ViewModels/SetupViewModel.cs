using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ExamLock.Models;

namespace ExamLock.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter)    => _execute();
        public void RaiseCanExecuteChanged()      => CommandManager.InvalidateRequerySuggested();
    }

    public class SetupViewModel : INotifyPropertyChanged
    {
        // ── Bindable fields ───────────────────────────────────────────────────

        private string _examBoard        = string.Empty;
        private string _examName         = string.Empty;
        private string _studentFullName  = string.Empty;
        private string _studentNumber    = string.Empty;
        private string _centreNumber     = string.Empty;
        private bool   _isTimedExam;
        private string _durationMinutes  = string.Empty;
        private string _errorMessage     = string.Empty;

        public string ExamBoard
        {
            get => _examBoard;
            set { _examBoard = value; OnPropertyChanged(); RaiseCommands(); }
        }

        public string ExamName
        {
            get => _examName;
            set { _examName = value; OnPropertyChanged(); RaiseCommands(); }
        }

        public string StudentFullName
        {
            get => _studentFullName;
            set { _studentFullName = value; OnPropertyChanged(); RaiseCommands(); }
        }

        public string StudentNumber
        {
            get => _studentNumber;
            set { _studentNumber = value; OnPropertyChanged(); RaiseCommands(); }
        }

        public string CentreNumber
        {
            get => _centreNumber;
            set { _centreNumber = value; OnPropertyChanged(); RaiseCommands(); }
        }

        public bool IsTimedExam
        {
            get => _isTimedExam;
            set { _isTimedExam = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimerFieldVisible)); RaiseCommands(); }
        }

        public bool TimerFieldVisible => _isTimedExam;

        public string DurationMinutes
        {
            get => _durationMinutes;
            set { _durationMinutes = value; OnPropertyChanged(); RaiseCommands(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        // ── Commands ──────────────────────────────────────────────────────────

        public RelayCommand StartExamCommand { get; }

        // ── Passwords — set from code-behind (PasswordBox can't bind) ─────────
        public string InvigilatorPassword        { get; set; } = string.Empty;
        public string InvigilatorPasswordConfirm { get; set; } = string.Empty;

        // ── Callback invoked when validation passes ───────────────────────────
        public Action<ExamSession>? OnExamStarted { get; set; }

        public SetupViewModel()
        {
            StartExamCommand = new RelayCommand(TryStartExam, CanStartExam);
        }

        private bool CanStartExam() =>
            !string.IsNullOrWhiteSpace(ExamBoard)       &&
            !string.IsNullOrWhiteSpace(ExamName)        &&
            !string.IsNullOrWhiteSpace(StudentFullName) &&
            !string.IsNullOrWhiteSpace(StudentNumber)   &&
            !string.IsNullOrWhiteSpace(CentreNumber)    &&
            (!IsTimedExam || !string.IsNullOrWhiteSpace(DurationMinutes));

        private void TryStartExam()
        {
            ErrorMessage = string.Empty;

            // Password validation
            if (InvigilatorPassword.Length < 4)
            {
                ErrorMessage = "Invigilator password must be at least 4 characters.";
                return;
            }

            if (InvigilatorPassword != InvigilatorPasswordConfirm)
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }

            int durationSeconds = 0;
            if (IsTimedExam)
            {
                if (!int.TryParse(DurationMinutes, out int mins) || mins < 1)
                {
                    ErrorMessage = "Duration must be a whole number of minutes (minimum 1).";
                    return;
                }
                durationSeconds = mins * 60;
            }

            var session = new ExamSession
            {
                ExamBoard           = ExamBoard.Trim(),
                ExamName            = ExamName.Trim(),
                StudentFullName     = StudentFullName.Trim(),
                StudentNumber       = StudentNumber.Trim(),
                ExamCentreNumber    = CentreNumber.Trim(),
                IsTimedExam         = IsTimedExam,
                DurationSeconds     = durationSeconds,
                RemainingSeconds    = durationSeconds,
                InvigilatorPassword = InvigilatorPassword,
                StartTime           = DateTime.UtcNow,
                InProgress          = true
            };

            OnExamStarted?.Invoke(session);
        }

        private void RaiseCommands() => StartExamCommand.RaiseCanExecuteChanged();

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
