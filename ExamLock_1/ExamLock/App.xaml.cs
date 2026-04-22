using System.Windows;
using ExamLock.Services;
using ExamLock.Views;

namespace ExamLock
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var recovered = AutoSaveService.TryLoadSession();
            if (recovered != null)
            {
                new ExamWindow(recovered, isCrashRecovery: true).Show();
            }
            else
            {
                new SetupWindow().Show();
            }
        }
    }
}
