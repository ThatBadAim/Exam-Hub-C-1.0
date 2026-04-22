using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExamLock.Views
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; } = Colors.Transparent;

        private static readonly string[] Swatches =
        {
            "#ffff99","#ffcc66","#ff9966","#ff6699","#cc99ff",
            "#99ccff","#66ffcc","#99ff99","#ccffcc","#ffffff",
            "#ffee00","#ff8800","#ff2200","#cc0066","#6600cc",
            "#0066ff","#00bbaa","#00cc44","#005500","#aaaaaa"
        };

        public ColorPickerWindow()
        {
            InitializeComponent();
            BuildSwatches();
        }

        private void BuildSwatches()
        {
            foreach (var hex in Swatches)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var btn   = new Button
                {
                    Width      = 36,
                    Height     = 36,
                    Margin     = new Thickness(3),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(0),
                    Cursor     = System.Windows.Input.Cursors.Hand,
                    Tag        = color
                };
                btn.Click += (_, _) =>
                {
                    SelectedColor = (Color)btn.Tag;
                    DialogResult = true;
                };
                SwatchPanel.Children.Add(btn);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) =>
            DialogResult = false;
    }
}
