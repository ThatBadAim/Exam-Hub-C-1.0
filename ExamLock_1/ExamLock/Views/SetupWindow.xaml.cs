using System.Windows;
using ExamLock.Models;
using ExamLock.Services;
using ExamLock.ViewModels;

namespace ExamLock.Views
{
    public partial class SetupWindow : Window
    {
        private readonly SetupViewModel _vm;

        public SetupWindow()
        {
            InitializeComponent();
            _vm = (SetupViewModel)DataContext;
            _vm.OnExamStarted = LaunchExam;
        }

        private void PasswordBox1_PasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.InvigilatorPassword = PasswordBox1.Password;

        private void PasswordBox2_PasswordChanged(object sender, RoutedEventArgs e) =>
            _vm.InvigilatorPasswordConfirm = PasswordBox2.Password;

        private async void LaunchExam(ExamSession session)
        {
            // Write initial crash flag / session file
            await AutoSaveService.SaveNowAsync(session);

            var examWindow = new ExamWindow(session, isCrashRecovery: false);
            examWindow.Show();
            Close();
        }
    }
}
