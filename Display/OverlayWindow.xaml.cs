using Microsoft.Win32;
using Owcounter.Model;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Owcounter.Display
{
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int HWND_TOPMOST = -1;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int F2_HOTKEY_ID = 9000;

        private MatchState? _matchState;
        private Dictionary<HeroName, HeroAnalysis>? _blueTeamAnalysis;
        private Dictionary<HeroName, HeroAnalysis>? _redTeamAnalysis;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const string OVERWATCH_WINDOW_TITLE = "Overwatch";
        private const string OVERWATCH_CLASS_NAME = "TankWindowClass";
        private readonly DispatcherTimer _positionTimer;
        private HwndSource? _source;
        private IntPtr _windowHandle;
        private readonly bool _devMode;

        public OverlayWindow(bool devMode)
        {
            _devMode = devMode;

            Resources.Add("ScaleConverter", new ScaleConverter());
            InitializeComponent();
            InitializeWindow();

            // Register for display settings changed event
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            Opacity = 0.95;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _positionTimer.Tick += OnPositionCheck;

            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    // Wait a moment for the display change to complete
                    Thread.Sleep(1000);

                    // Update window bounds to match primary screen
                    SetToScreenResolution();

                    // If window is visible, reposition it
                    if (IsVisible)
                    {
                        var overwatchHandle = FindOverwatchWindow();
                        if (overwatchHandle != IntPtr.Zero)
                        {
                            PositionOverOverwatch(overwatchHandle);
                        }
                        else if (!_devMode)
                        {
                            // Hide the window if Overwatch isn't found and we're not in dev mode
                            Visibility = Visibility.Hidden;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error handling display settings change: {ex.Message}");
                }
            }));
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;

            // Set extended window styles
            var extendedStyle = GetWindowLong(_windowHandle, GWL_EXSTYLE);
            SetWindowLong(_windowHandle, GWL_EXSTYLE, extendedStyle |
                WS_EX_LAYERED |      // For transparency
                WS_EX_TRANSPARENT |   // Click-through
                WS_EX_NOACTIVATE |    // Prevent activation
                WS_EX_TOOLWINDOW      // No taskbar icon
            );

            SetWindowPos(_windowHandle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);

            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HandleMessages);

            RegisterHotKey(_windowHandle, F2_HOTKEY_ID, 0, 0x71);

            Visibility = _devMode ? Visibility.Visible : Visibility.Hidden;
            _positionTimer.Start();
        }

        private IntPtr FindOverwatchWindow()
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, null, OVERWATCH_WINDOW_TITLE)) != IntPtr.Zero)
            {
                var className = new System.Text.StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);

                if (className.ToString() == OVERWATCH_CLASS_NAME)
                {
                    return hwnd;
                }
            }
            return IntPtr.Zero;
        }

        private void OnPositionCheck(object? sender, EventArgs e)
        {
            var overwatchHandle = FindOverwatchWindow();
            bool overwatchRunning = overwatchHandle != IntPtr.Zero;

            if (!overwatchRunning && !_devMode)
            {
                if (IsVisible)
                {
                    Visibility = Visibility.Hidden;
                }
            }
            else if (overwatchRunning && IsVisible)
            {
                PositionOverOverwatch(overwatchHandle);
            }
        }

        private void PositionOverOverwatch(IntPtr overwatchHandle)
        {
            if (GetWindowRect(overwatchHandle, out RECT overwatchRect))
            {
                // Get the screen where Overwatch is running
                var screen = Screen.FromHandle(overwatchHandle);

                int width = overwatchRect.Right - overwatchRect.Left;
                int height = overwatchRect.Bottom - overwatchRect.Top;

                if (Left != overwatchRect.Left || Top != overwatchRect.Top ||
                    Width != width || Height != height)
                {
                    // Ensure the window is on the correct screen and has the right size
                    MoveWindow(_windowHandle, overwatchRect.Left, overwatchRect.Top, width, height, true);
                    SetWindowPos(_windowHandle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);

                    // Update window position properties
                    Left = overwatchRect.Left;
                    Top = overwatchRect.Top;
                    Width = width;
                    Height = height;
                }
            }
            else
            {
                Logger.Log("Failed to get Overwatch window rectangle");
            }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _positionTimer.Stop();

            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, F2_HOTKEY_ID);
            }
            _source?.RemoveHook(HandleMessages);
        }

        private void InitializeWindow()
        {
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Topmost = true;
            Focusable = false;
            ShowActivated = false;
            IsHitTestVisible = false;
        }

        private void SetToScreenResolution()
        {
            try
            {
                // Get the screen where Overwatch is running
                var overwatchHandle = FindOverwatchWindow();
                if (overwatchHandle != IntPtr.Zero)
                {
                    var screen = Screen.FromHandle(overwatchHandle);
                    Width = screen.Bounds.Width;
                    Height = screen.Bounds.Height;
                }
                else
                {
                    // Fallback to primary screen if Overwatch window isn't found
                    Width = SystemParameters.PrimaryScreenWidth;
                    Height = SystemParameters.PrimaryScreenHeight;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error setting screen resolution: {ex.Message}");
                // Fallback to primary screen
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
            }
        }

        public void OnScreenshotProcessed(object sender, ScreenshotProcessingResponse response)
        {
            Dispatcher.Invoke(() =>
            {
                _matchState = response.MatchState;
                _blueTeamAnalysis = response.BlueTeamAnalysis;
                _redTeamAnalysis = response.RedTeamAnalysis;

                if (_blueTeamAnalysis != null)
                    BlueTeamPanel.UpdateCompositions(_blueTeamAnalysis);

                if (_redTeamAnalysis != null)
                    RedTeamPanel.UpdateCompositions(_redTeamAnalysis);

                if (_matchState?.PlayerHero != null && _blueTeamAnalysis != null)
                {
                    SwapSuggestionsPanel.UpdateSuggestions(_matchState.PlayerHero, _blueTeamAnalysis, _matchState.Map);
                }
            });
        }

        public void ToggleVisibility()
        {
            var overwatchHandle = FindOverwatchWindow();
            bool overwatchRunning = overwatchHandle != IntPtr.Zero;

            if (!_devMode && !overwatchRunning)
            {
                return;
            }

            Visibility = IsVisible ? Visibility.Hidden : Visibility.Visible;

            if (IsVisible && overwatchRunning)
            {
                PositionOverOverwatch(overwatchHandle);
            }
        }

        private IntPtr HandleMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_HOTKEY when wParam.ToInt32() == F2_HOTKEY_ID:
                    ToggleVisibility();
                    handled = true;
                    break;
                case WM_DISPLAYCHANGE:
                    OnDisplaySettingsChanged(this, EventArgs.Empty);
                    handled = true;
                    break;
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            base.OnClosed(e);
        }
    }

    public class ScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double size && parameter is string percentStr &&
double.TryParse(percentStr, out double percent))
            {
                return size * percent;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}