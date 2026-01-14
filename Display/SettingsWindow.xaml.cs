using Owmeta.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Owmeta.Display
{
    public partial class SettingsWindow : Window
    {
        private Button? _capturingButton;
        private int _swapKey;
        private int _teamKey;
        private int _screenshotKey;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            MinScoreF2Box.Text = AppSettings.Instance.MinScoreF2.ToString();
            MinScoreF3Box.Text = AppSettings.Instance.MinScoreF3.ToString();
            TabScreenshotCheckBox.IsChecked = AppSettings.Instance.TabScreenshotEnabled;

            _swapKey = AppSettings.Instance.SwapSuggestionsKey;
            _teamKey = AppSettings.Instance.TeamCompositionKey;
            _screenshotKey = AppSettings.Instance.ScreenshotKey;

            SwapKeyButton.Content = KeyHelper.GetKeyName(_swapKey);
            TeamKeyButton.Content = KeyHelper.GetKeyName(_teamKey);
            ScreenshotKeyButton.Content = KeyHelper.GetKeyName(_screenshotKey);
        }

        private void SwapKeyButton_Click(object sender, RoutedEventArgs e)
        {
            StartKeyCapture(SwapKeyButton);
        }

        private void TeamKeyButton_Click(object sender, RoutedEventArgs e)
        {
            StartKeyCapture(TeamKeyButton);
        }

        private void ScreenshotKeyButton_Click(object sender, RoutedEventArgs e)
        {
            StartKeyCapture(ScreenshotKeyButton);
        }

        private void StartKeyCapture(Button button)
        {
            if (_capturingButton != null)
            {
                // Cancel previous capture
                RestoreButtonContent(_capturingButton);
            }

            _capturingButton = button;
            button.Content = "Press key...";
            button.Focus();
        }

        private void RestoreButtonContent(Button button)
        {
            if (button == SwapKeyButton)
                button.Content = KeyHelper.GetKeyName(_swapKey);
            else if (button == TeamKeyButton)
                button.Content = KeyHelper.GetKeyName(_teamKey);
            else if (button == ScreenshotKeyButton)
                button.Content = KeyHelper.GetKeyName(_screenshotKey);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturingButton == null) return;

            // Ignore modifier keys alone
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            // Handle Escape to cancel
            if (e.Key == Key.Escape)
            {
                CancelKeyCapture();
                e.Handled = true;
                return;
            }

            int virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);
            SetCapturedKey(virtualKey);
            e.Handled = true;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_capturingButton == null) return;

            // Only capture XButton1 and XButton2 (Mouse4/Mouse5)
            if (e.ChangedButton == MouseButton.XButton1)
            {
                SetCapturedKey(KeyHelper.MOUSE_XBUTTON1);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                SetCapturedKey(KeyHelper.MOUSE_XBUTTON2);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Right)
            {
                // Left/right click cancels capture (so user can click elsewhere)
                CancelKeyCapture();
            }
        }

        private void SetCapturedKey(int keyCode)
        {
            if (_capturingButton == null) return;

            if (_capturingButton == SwapKeyButton)
            {
                _swapKey = keyCode;
            }
            else if (_capturingButton == TeamKeyButton)
            {
                _teamKey = keyCode;
            }
            else if (_capturingButton == ScreenshotKeyButton)
            {
                _screenshotKey = keyCode;
            }

            _capturingButton.Content = KeyHelper.GetKeyName(keyCode);
            _capturingButton = null;
        }

        private void CancelKeyCapture()
        {
            if (_capturingButton == null) return;

            RestoreButtonContent(_capturingButton);
            _capturingButton = null;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MinScoreF2Box.Text, out int minF2))
            {
                AppSettings.Instance.MinScoreF2 = minF2;
            }

            if (int.TryParse(MinScoreF3Box.Text, out int minF3))
            {
                AppSettings.Instance.MinScoreF3 = minF3;
            }

            AppSettings.Instance.TabScreenshotEnabled = TabScreenshotCheckBox.IsChecked ?? false;
            AppSettings.Instance.SwapSuggestionsKey = _swapKey;
            AppSettings.Instance.TeamCompositionKey = _teamKey;
            AppSettings.Instance.ScreenshotKey = _screenshotKey;

            AppSettings.Instance.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
