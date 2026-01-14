using Microsoft.Win32;
using Owmeta.Model;
using Owmeta.Services;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Owmeta.Display
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

        // Hotkey IDs
        private const int SWAP_HOTKEY_ID = 9000;
        private const int TEAM_HOTKEY_ID = 9003;
        private const int F5_HOTKEY_ID = 9001;  // Previous screenshot (dev mode)
        private const int F6_HOTKEY_ID = 9002;  // Next screenshot (dev mode)

        // Low-level mouse hook constants
        private const int WH_MOUSE_LL = 14;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int XBUTTON1 = 0x0001;
        private const int XBUTTON2 = 0x0002;

        // Low-level keyboard hook constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

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

        // Low-level hooks
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookEx")]
        private static extern IntPtr SetWindowsHookExKeyboard(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public System.Drawing.Point pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

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
        private ScreenshotMonitoringService? _screenshotService;

        // Keybinding state
        private int _currentSwapKey;
        private int _currentTeamKey;
        private bool _swapHotkeyRegistered;
        private bool _teamHotkeyRegistered;
        private int _currentScreenshotKey;

        // Mouse hook
        private IntPtr _mouseHookId = IntPtr.Zero;
        private LowLevelMouseProc? _mouseProc;

        // Keyboard hook for screenshot key (detects both press and release without blocking)
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _keyboardProc;
        private volatile bool _hudWasVisibleBeforeScreenshot;
        private Visibility _swapVisibilityBeforeScreenshot;
        private Visibility _compVisibilityBeforeScreenshot;
        private int _screenshotKeyPressed; // 0 = not pressed, 1 = pressed (use Interlocked for thread safety)

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

        public void SetScreenshotService(ScreenshotMonitoringService service)
        {
            _screenshotService = service;
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

            // Register custom keybindings
            UpdateHotkeyRegistrations();

            // Register F5/F6 for screenshot navigation in dev mode
            if (_devMode)
            {
                var f5Result = RegisterHotKey(_windowHandle, F5_HOTKEY_ID, 0, 0x74);  // F5
                var f6Result = RegisterHotKey(_windowHandle, F6_HOTKEY_ID, 0, 0x75);  // F6
                Logger.Log($"[DEV] F5/F6 hotkeys registered: F5={f5Result}, F6={f6Result}");
            }

            // Register TAB hotkey for auto-capture if enabled
            UpdateScreenshotHotkeyRegistration();

            // Set up mouse hook if needed
            SetupMouseHookIfNeeded();

            // In dev mode, set resolution immediately since Overwatch isn't running
            if (_devMode)
            {
                SetToScreenResolution();
            }

            Visibility = _devMode ? Visibility.Visible : Visibility.Hidden;
            _positionTimer.Start();
        }

        private void SetupMouseHookIfNeeded()
        {
            bool needMouseHook = KeyHelper.IsMouseButton(AppSettings.Instance.SwapSuggestionsKey) ||
                                 KeyHelper.IsMouseButton(AppSettings.Instance.TeamCompositionKey);

            if (needMouseHook && _mouseHookId == IntPtr.Zero)
            {
                _mouseProc = MouseHookCallback;
                _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
                Logger.Log("Mouse hook installed for side button support");
            }
            else if (!needMouseHook && _mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
                _mouseProc = null;
                Logger.Log("Mouse hook removed");
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_XBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int xButton = (int)(hookStruct.mouseData >> 16);

                int pressedButton = xButton == XBUTTON1 ? KeyHelper.MOUSE_XBUTTON1 : KeyHelper.MOUSE_XBUTTON2;

                if (pressedButton == AppSettings.Instance.SwapSuggestionsKey)
                {
                    Dispatcher.BeginInvoke(new Action(() => ToggleVisibility()));
                }
                else if (pressedButton == AppSettings.Instance.TeamCompositionKey)
                {
                    Dispatcher.BeginInvoke(new Action(() => ToggleCompositionDashboard()));
                }
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        public void UpdateHotkeyRegistrations()
        {
            if (_windowHandle == IntPtr.Zero) return;

            var settings = AppSettings.Instance;

            // Update Swap Suggestions hotkey
            if (!KeyHelper.IsMouseButton(settings.SwapSuggestionsKey))
            {
                if (_swapHotkeyRegistered && _currentSwapKey != settings.SwapSuggestionsKey)
                {
                    UnregisterHotKey(_windowHandle, SWAP_HOTKEY_ID);
                    _swapHotkeyRegistered = false;
                }

                if (!_swapHotkeyRegistered && settings.SwapSuggestionsKey > 0)
                {
                    RegisterHotKey(_windowHandle, SWAP_HOTKEY_ID, 0, (uint)settings.SwapSuggestionsKey);
                    _swapHotkeyRegistered = true;
                    _currentSwapKey = settings.SwapSuggestionsKey;
                    Logger.Log($"Swap hotkey registered: {KeyHelper.GetKeyName(settings.SwapSuggestionsKey)}");
                }
            }
            else if (_swapHotkeyRegistered)
            {
                UnregisterHotKey(_windowHandle, SWAP_HOTKEY_ID);
                _swapHotkeyRegistered = false;
            }

            // Update Team Composition hotkey
            if (!KeyHelper.IsMouseButton(settings.TeamCompositionKey))
            {
                if (_teamHotkeyRegistered && _currentTeamKey != settings.TeamCompositionKey)
                {
                    UnregisterHotKey(_windowHandle, TEAM_HOTKEY_ID);
                    _teamHotkeyRegistered = false;
                }

                if (!_teamHotkeyRegistered && settings.TeamCompositionKey > 0)
                {
                    RegisterHotKey(_windowHandle, TEAM_HOTKEY_ID, 0, (uint)settings.TeamCompositionKey);
                    _teamHotkeyRegistered = true;
                    _currentTeamKey = settings.TeamCompositionKey;
                    Logger.Log($"Team hotkey registered: {KeyHelper.GetKeyName(settings.TeamCompositionKey)}");
                }
            }
            else if (_teamHotkeyRegistered)
            {
                UnregisterHotKey(_windowHandle, TEAM_HOTKEY_ID);
                _teamHotkeyRegistered = false;
            }

            // Update mouse hook
            SetupMouseHookIfNeeded();
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

            // Remove mouse hook
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }

            // Remove keyboard hook
            RemoveKeyboardHook();

            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, SWAP_HOTKEY_ID);
                UnregisterHotKey(_windowHandle, TEAM_HOTKEY_ID);
                if (_devMode)
                {
                    UnregisterHotKey(_windowHandle, F5_HOTKEY_ID);
                    UnregisterHotKey(_windowHandle, F6_HOTKEY_ID);
                }
            }
            _source?.RemoveHook(HandleMessages);
        }

        public void UpdateScreenshotHotkeyRegistration()
        {
            if (_windowHandle == IntPtr.Zero) return;

            var settings = AppSettings.Instance;
            bool shouldBeEnabled = settings.TabScreenshotEnabled && settings.ScreenshotKey > 0;

            if (shouldBeEnabled && _keyboardHookId == IntPtr.Zero)
            {
                // Install keyboard hook to detect screenshot key press/release without blocking
                _keyboardProc = KeyboardHookCallback;
                _keyboardHookId = SetWindowsHookExKeyboard(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
                _currentScreenshotKey = settings.ScreenshotKey;
                Logger.Log($"Screenshot keyboard hook installed for: {KeyHelper.GetKeyName(settings.ScreenshotKey)}");
            }
            else if (!shouldBeEnabled && _keyboardHookId != IntPtr.Zero)
            {
                RemoveKeyboardHook();
                Logger.Log("Screenshot keyboard hook removed");
            }
            else if (shouldBeEnabled && _currentScreenshotKey != settings.ScreenshotKey)
            {
                // Key changed, just update the tracked key (hook handles all keys)
                _currentScreenshotKey = settings.ScreenshotKey;
                Logger.Log($"Screenshot key changed to: {KeyHelper.GetKeyName(settings.ScreenshotKey)}");
            }
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
                    // Fallback to primary screen actual pixels (not WPF logical units)
                    var primaryScreen = Screen.PrimaryScreen;
                    if (primaryScreen != null)
                    {
                        Left = primaryScreen.Bounds.Left;
                        Top = primaryScreen.Bounds.Top;
                        Width = primaryScreen.Bounds.Width;
                        Height = primaryScreen.Bounds.Height;
                        Logger.Log($"Using primary screen: pos=({Left},{Top}) size={Width}x{Height}");
                    }
                    else
                    {
                        Left = 0;
                        Top = 0;
                        Width = SystemParameters.PrimaryScreenWidth;
                        Height = SystemParameters.PrimaryScreenHeight;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error setting screen resolution: {ex.Message}");
                // Fallback to primary screen
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
            }
        }

        public void OnScreenshotProcessed(object sender, ScreenshotProcessingResponse response)
        {
            Dispatcher.Invoke(() =>
            {
                _matchState = response.MatchState;

                // Filter out Unknown/Hidden heroes, keeping only recognized ones
                var filteredBlue = FilterUnknownHeroes(response.BlueTeamAnalysis);
                var filteredRed = FilterUnknownHeroes(response.RedTeamAnalysis);

                // Track if we're using cached data due to detection failure
                bool usingCachedData = false;

                // Only update if we have valid data (at least some recognized heroes)
                // This preserves previous known data when detection fails
                if (filteredBlue.Count > 0)
                    _blueTeamAnalysis = filteredBlue;
                else if (_blueTeamAnalysis != null)
                    usingCachedData = true;

                if (filteredRed.Count > 0)
                    _redTeamAnalysis = filteredRed;
                else if (_redTeamAnalysis != null)
                    usingCachedData = true;

                if (_blueTeamAnalysis != null)
                    BlueTeamPanel.UpdateCompositions(_blueTeamAnalysis);

                if (_redTeamAnalysis != null)
                    RedTeamPanel.UpdateCompositions(_redTeamAnalysis);

                if (_matchState?.PlayerHero != null && _blueTeamAnalysis != null)
                {
                    SwapSuggestionsPanel.UpdateSuggestions(_matchState.PlayerHero, _blueTeamAnalysis, _matchState.Map);

                    // Show appropriate status indicator
                    if (usingCachedData)
                        DataFreshnessIndicator.SetStaleWarning();
                    else
                        DataFreshnessIndicator.MarkUpdated();
                }

                // Update composition dashboard (F3 layout)
                if (_blueTeamAnalysis != null && _redTeamAnalysis != null)
                {
                    CompositionDashboard.Update(_blueTeamAnalysis, _redTeamAnalysis);
                }
            });
        }

        private static Dictionary<HeroName, HeroAnalysis> FilterUnknownHeroes(
            Dictionary<HeroName, HeroAnalysis>? data)
        {
            if (data == null) return new Dictionary<HeroName, HeroAnalysis>();

            return data
                .Where(kvp => kvp.Key != HeroName.Unknown &&
                              kvp.Key != HeroName.Hidden &&
                              kvp.Key != HeroName.NameUnspecified)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public void OnAnalysisStarted()
        {
            Dispatcher.Invoke(() =>
            {
                DataFreshnessIndicator.SetAnalyzing();
            });
        }

        public void OnAnalysisError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                DataFreshnessIndicator.SetError(message);
            });
        }

        public void RefreshDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                if (_matchState?.PlayerHero != null && _blueTeamAnalysis != null)
                {
                    SwapSuggestionsPanel.UpdateSuggestions(_matchState.PlayerHero, _blueTeamAnalysis, _matchState.Map);
                }

                if (_blueTeamAnalysis != null && _redTeamAnalysis != null)
                {
                    CompositionDashboard.Update(_blueTeamAnalysis, _redTeamAnalysis);
                }

                // Update hotkey registrations based on current settings
                UpdateHotkeyRegistrations();
                UpdateScreenshotHotkeyRegistration();
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

            // If F3 layout is visible, switch to F2
            if (CompositionLayout.Visibility == Visibility.Visible)
            {
                CompositionLayout.Visibility = Visibility.Hidden;
                SwapLayout.Visibility = Visibility.Visible;
            }
            else
            {
                // Toggle SwapLayout (F2)
                SwapLayout.Visibility = SwapLayout.Visibility == Visibility.Visible
                    ? Visibility.Hidden
                    : Visibility.Visible;
            }

            UpdateWindowVisibility();

            if (IsVisible && overwatchRunning)
            {
                PositionOverOverwatch(overwatchHandle);
            }
        }

        public void ToggleCompositionDashboard()
        {
            var overwatchHandle = FindOverwatchWindow();
            bool overwatchRunning = overwatchHandle != IntPtr.Zero;

            if (!_devMode && !overwatchRunning)
            {
                return;
            }

            // If F2 layout is visible, switch to F3
            if (SwapLayout.Visibility == Visibility.Visible)
            {
                SwapLayout.Visibility = Visibility.Hidden;
                CompositionLayout.Visibility = Visibility.Visible;
            }
            else
            {
                // Toggle CompositionLayout (F3)
                CompositionLayout.Visibility = CompositionLayout.Visibility == Visibility.Visible
                    ? Visibility.Hidden
                    : Visibility.Visible;
            }

            UpdateWindowVisibility();

            if (IsVisible && overwatchRunning)
            {
                PositionOverOverwatch(overwatchHandle);
            }
        }

        private void UpdateWindowVisibility()
        {
            // Show window if any layout is visible
            bool anyVisible = SwapLayout.Visibility == Visibility.Visible ||
                              CompositionLayout.Visibility == Visibility.Visible;
            Visibility = anyVisible ? Visibility.Visible : Visibility.Hidden;
        }

        private async void CaptureScreenshotWithHiddenHud()
        {
            // Visibility state was already captured in KeyboardHookCallback before dispatch
            // Hide the HUD so it doesn't appear in the screenshot
            if (_hudWasVisibleBeforeScreenshot)
            {
                SwapLayout.Visibility = Visibility.Hidden;
                CompositionLayout.Visibility = Visibility.Hidden;
                UpdateWindowVisibility();

                // Wait for the UI to update before capturing
                await Task.Delay(50);
            }

            // Capture the screenshot
            _screenshotService?.CaptureAndProcessScreenshot();
        }

        private void RemoveKeyboardHook()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
                _keyboardProc = null;
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)hookStruct.vkCode;

                // Only handle our screenshot key
                if (vkCode == _currentScreenshotKey)
                {
                    if (wParam == (IntPtr)WM_KEYDOWN)
                    {
                        // Thread-safe check-and-set: only proceed if we're first to set the flag
                        if (Interlocked.CompareExchange(ref _screenshotKeyPressed, 1, 0) == 0)
                        {
                            // Capture visibility state NOW (on hook thread) before async dispatch
                            // to avoid race condition if key is released very quickly
                            var swapVis = Visibility.Hidden;
                            var compVis = Visibility.Hidden;
                            Dispatcher.Invoke(() =>
                            {
                                swapVis = SwapLayout.Visibility;
                                compVis = CompositionLayout.Visibility;
                            });
                            _swapVisibilityBeforeScreenshot = swapVis;
                            _compVisibilityBeforeScreenshot = compVis;
                            _hudWasVisibleBeforeScreenshot = swapVis == Visibility.Visible || compVis == Visibility.Visible;

                            Logger.Log($"Screenshot key pressed (vkCode={vkCode})");
                            Dispatcher.BeginInvoke(new Action(() => CaptureScreenshotWithHiddenHud()));
                        }
                    }
                    else if (wParam == (IntPtr)WM_KEYUP)
                    {
                        // Thread-safe reset: only proceed if flag was set
                        if (Interlocked.CompareExchange(ref _screenshotKeyPressed, 0, 1) == 1)
                        {
                            Logger.Log("Screenshot key released - restoring HUD");
                            // Capture the saved visibility state for the closure
                            var wasVisible = _hudWasVisibleBeforeScreenshot;
                            var swapVis = _swapVisibilityBeforeScreenshot;
                            var compVis = _compVisibilityBeforeScreenshot;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (wasVisible)
                                {
                                    SwapLayout.Visibility = swapVis;
                                    CompositionLayout.Visibility = compVis;
                                    UpdateWindowVisibility();
                                }
                            }));
                        }
                    }
                }
            }

            // IMPORTANT: Always pass the key to the next hook so it reaches the game
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr HandleMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_HOTKEY when wParam.ToInt32() == SWAP_HOTKEY_ID:
                    ToggleVisibility();
                    handled = true;
                    break;
                case WM_HOTKEY when wParam.ToInt32() == TEAM_HOTKEY_ID:
                    ToggleCompositionDashboard();
                    handled = true;
                    break;
                case WM_HOTKEY when wParam.ToInt32() == F5_HOTKEY_ID:
                    Logger.Log("[DEV] F5 pressed - previous screenshot");
                    _screenshotService?.PreviousScreenshot();
                    handled = true;
                    break;
                case WM_HOTKEY when wParam.ToInt32() == F6_HOTKEY_ID:
                    Logger.Log("[DEV] F6 pressed - next screenshot");
                    _screenshotService?.NextScreenshot();
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
