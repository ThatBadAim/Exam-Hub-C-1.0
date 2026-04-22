using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExamLock.Models;
using ExamLock.Services;
using ExamLock.ViewModels;

namespace ExamLock.Views
{
    public partial class ExamWindow : Window
    {
        private readonly ExamViewModel   _vm;
        private readonly LockdownService _lockdown;
        private readonly HardwareService _hardware;
        private readonly AutoSaveService _autoSave;
        private readonly AuditLog        _auditLog;
        private bool _suppressSelectionUpdate;

        public ExamWindow(ExamSession session, bool isCrashRecovery)
        {
            InitializeComponent();

            _auditLog = new AuditLog();
            _autoSave = new AutoSaveService();
            _lockdown = new LockdownService(_auditLog);
            _hardware = new HardwareService(_auditLog);

            _vm = new ExamViewModel(session, _auditLog, _autoSave)
            {
                GetRtfContent  = GetRtfFromEditor,
                OnWriteOutput  = FinishExam
            };

            DataContext = _vm;

            // Restore RTF content after crash recovery
            if (isCrashRecovery && !string.IsNullOrEmpty(session.RtfContent))
            {
                Loaded += (_, _) => SetRtfContent(session.RtfContent);
            }

            Loaded += OnWindowLoaded;
            Closing += (_, e) => e.Cancel = true; // prevent close except via supervised exit
        }

        // ── Window loaded ─────────────────────────────────────────────────────

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // Block screen capture
            LockdownService.SetScreenCaptureBlock(hwnd);

            // Keyboard hook
            _lockdown.InstallKeyboardHook();

            // Process monitor
            _lockdown.StartProcessMonitor();

            // Taskbar, sleep, GameDVR
            _lockdown.HideTaskbar();
            _lockdown.PreventSleep();
            _lockdown.DisableGameDVR();

            // Network (best-effort)
            _hardware.DisableNetworkInterfaces();

            // Start VM (timer + auto-save)
            var session = _vm.Session;
            bool isCrash = AutoSaveService.TryLoadSession()?.InProgress == true
                           && session.StartTime < DateTime.UtcNow.AddSeconds(-5);
            _vm.Start(isCrashRecovery: isCrash);
        }

        // ── Paste intercept — block external clipboard ────────────────────────

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (e.Key == Key.V &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                // Allow internal paste only
                try
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                        EditorBox.Selection.Text = text;
                }
                catch { }
            }
        }

        // ── RTF helpers ───────────────────────────────────────────────────────

        private string GetRtfFromEditor()
        {
            try
            {
                var range = new TextRange(
                    EditorBox.Document.ContentStart,
                    EditorBox.Document.ContentEnd);
                using var ms = new MemoryStream();
                range.Save(ms, DataFormats.Rtf);
                ms.Position = 0;
                return new StreamReader(ms).ReadToEnd();
            }
            catch { return string.Empty; }
        }

        private void SetRtfContent(string rtf)
        {
            try
            {
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rtf));
                var range = new TextRange(
                    EditorBox.Document.ContentStart,
                    EditorBox.Document.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
            }
            catch { }
        }

        // ── Formatting buttons ────────────────────────────────────────────────

        private void BoldButton_Checked(object sender, RoutedEventArgs e)
        {
            EditorBox.Focus();
            var val = BoldButton.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            EditorBox.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, val);
        }

        private void ItalicButton_Checked(object sender, RoutedEventArgs e)
        {
            EditorBox.Focus();
            var val = ItalicButton.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            EditorBox.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, val);
        }

        private void UnderlineButton_Checked(object sender, RoutedEventArgs e)
        {
            EditorBox.Focus();
            var val = UnderlineButton.IsChecked == true
                ? TextDecorations.Underline
                : new TextDecorationCollection();
            EditorBox.Selection.ApplyPropertyValue(
                Inline.TextDecorationsProperty, val);
        }

        private void FontCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (EditorBox == null) return;
            var item = FontCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item == null) return;
            EditorBox.Focus();
            EditorBox.Selection.ApplyPropertyValue(
                TextElement.FontFamilyProperty,
                new FontFamily(item.Content.ToString()!));
        }

        private void SizeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (EditorBox == null) return;
            var item = SizeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (item == null) return;
            if (double.TryParse(item.Content.ToString(), out double size))
            {
                EditorBox.Focus();
                EditorBox.Selection.ApplyPropertyValue(
                    TextElement.FontSizeProperty, size);
            }
        }

        // ── Selection changed — update toolbar toggle state ───────────────────

        private void EditorBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressSelectionUpdate) return;
            _suppressSelectionUpdate = true;

            var weight = EditorBox.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            BoldButton.IsChecked = weight != DependencyProperty.UnsetValue
                && (FontWeight)weight == FontWeights.Bold;

            var style = EditorBox.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            ItalicButton.IsChecked = style != DependencyProperty.UnsetValue
                && (FontStyle)style == FontStyles.Italic;

            var deco = EditorBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            UnderlineButton.IsChecked = deco != DependencyProperty.UnsetValue
                && deco?.ToString()?.Contains("Underline", StringComparison.OrdinalIgnoreCase) == true;

            _suppressSelectionUpdate = false;
        }

        // ── Text changed — word count ─────────────────────────────────────────

        private void EditorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var plain = new TextRange(
                EditorBox.Document.ContentStart,
                EditorBox.Document.ContentEnd).Text;
            _vm.UpdateWordCount(plain);
        }

        // ── High contrast ─────────────────────────────────────────────────────

        private void HighContrast_Checked(object sender, RoutedEventArgs e)
        {
            EditorBox.Background = Brushes.Black;
            EditorBox.Foreground = Brushes.White;
        }

        private void HighContrast_Unchecked(object sender, RoutedEventArgs e)
        {
            EditorBox.Background = Brushes.White;
            EditorBox.Foreground = Brushes.Black;
        }

        // ── Colour veil ───────────────────────────────────────────────────────

        private void VeilButton_Click(object sender, RoutedEventArgs e)
        {
            VeilPopup.PlacementTarget = (UIElement)sender;
            VeilPopup.IsOpen = true;
        }

        private void VeilPreset_Click(object sender, RoutedEventArgs e)
        {
            VeilPopup.IsOpen = false;
            var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "Transparent";
            if (tag == "Transparent")
            {
                VeilOverlay.Fill = null;
                return;
            }
            var color = (Color)ColorConverter.ConvertFromString(tag);
            VeilOverlay.Fill = new SolidColorBrush(color);
        }

        private void VeilCustom_Click(object sender, RoutedEventArgs e)
        {
            VeilPopup.IsOpen = false;
            var picker = new ColorPickerWindow();
            picker.Owner = this;
            if (picker.ShowDialog() == true)
            {
                VeilOverlay.Fill = new SolidColorBrush(picker.SelectedColor);
            }
        }

        // ── Exit flow ─────────────────────────────────────────────────────────

        private void ExitExamButton_Click(object sender, RoutedEventArgs e)
        {
            ExitPasswordBox.Password = string.Empty;
            _vm.ShowExitOverlay();
        }

        private void CancelExit_Click(object sender, RoutedEventArgs e)
        {
            _vm.CancelExitOverlay();
            ExitPasswordBox.Password = string.Empty;
        }

        private void ConfirmExit_Click(object sender, RoutedEventArgs e)
        {
            _vm.SubmitExitPassword(ExitPasswordBox.Password);
            ExitPasswordBox.Password = string.Empty;
        }

        // ── Finish exam — called by VM on correct password ────────────────────

        private void FinishExam(ExamSession session)
        {
            // Stop all lockdown
            _lockdown.StopProcessMonitor();
            _lockdown.RemoveKeyboardHook();
            _lockdown.ShowTaskbar();
            _lockdown.RestoreSleep();
            _lockdown.RestoreGameDVR();
            _hardware.RestoreNetworkInterfaces();

            // Build file names
            string date    = DateTime.Now.ToString("yyyy-MM-dd");
            string safe    = $"{session.ExamBoard}_{session.ExamName}_{session.StudentNumber}_{date}";
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string rtfPath = Path.Combine(desktop, $"{safe}.rtf");
            string logPath = Path.Combine(desktop, $"{safe}_AuditLog.txt");

            // Save RTF
            try
            {
                File.WriteAllText(rtfPath, GetRtfFromEditor());
            }
            catch (Exception ex)
            {
                _auditLog.Add($"RTF save error: {ex.Message}");
            }

            // Save audit log
            _vm.AuditLog.Add("RTF output saved.");
            _vm.AuditLog.WriteToDisk(logPath);

            // Delete crash flag
            AutoSaveService.DeleteSession();

            // Show complete overlay (no new window — in-place)
            _vm.ExitOverlayVisible = false;
            CompleteStudentText.Text = $"Student: {session.StudentFullName}  ({session.StudentNumber})";
            CompleteExamText.Text    = $"{session.ExamBoard} — {session.ExamName}";
            CompleteRtfPath.Text     = rtfPath;
            CompleteLogPath.Text     = logPath;
            CompleteOverlay.Visibility = Visibility.Visible;
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();
    }
}
