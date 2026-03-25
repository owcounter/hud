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
        private const int WM_INPUT = 0x00FF;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

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

        // Raw Input constants
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const int RID_INPUT = 0x10000003;
        private const int RIM_TYPEKEYBOARD = 1;

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

        // Low-level mouse hook
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        // Raw Input API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices([MarshalAs(UnmanagedType.LPArray)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public int dwType;
            public int dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER header;
            public RAWKEYBOARD keyboard;
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

        // Mouse hook (for side button support — no Raw Input equivalent for XButtons)
        private IntPtr _mouseHookId = IntPtr.Zero;
        private LowLevelMouseProc? _mouseProc;

        // Raw Input keyboard state (replaces WH_KEYBOARD_LL hook — zero input lag)
        private bool _rawInputRegistered;
        private IntPtr _rawInputBuffer = IntPtr.Zero;  // Pre-allocated buffer to avoid per-keystroke allocation
        private uint _rawInputBufferSize;
        private bool _hudWasVisibleBeforeScreenshot;
        private bool _screenshotKeyPressed;
        private Visibility _swapVisibilityBeforeScreenshot;
        private Visibility _compVisibilityBeforeScreenshot;

        // Track hotkey pressed state to prevent flickering from key repeat
        private bool _swapKeyHeld;
        private bool _teamKeyHeld;

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

            // Register Raw Input for keyboard (replaces WH_KEYBOARD_LL — no input lag)
            RegisterRawInput();

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

            // Unregister old hotkeys if they were registered (we now use keyboard hook instead)
            if (_swapHotkeyRegistered)
            {
                UnregisterHotKey(_windowHandle, SWAP_HOTKEY_ID);
                _swapHotkeyRegistered = false;
            }
            if (_teamHotkeyRegistered)
            {
                UnregisterHotKey(_windowHandle, TEAM_HOTKEY_ID);
                _teamHotkeyRegistered = false;
            }

            // Track current keys for keyboard hook
            _currentSwapKey = settings.SwapSuggestionsKey;
            _currentTeamKey = settings.TeamCompositionKey;
            Logger.Log($"Hotkeys updated: Swap={KeyHelper.GetKeyName(settings.SwapSuggestionsKey)}, Team={KeyHelper.GetKeyName(settings.TeamCompositionKey)}");

            // Update mouse hook for mouse button bindings
            SetupMouseHookIfNeeded();

            // Update tracked keys for Raw Input handler
            UpdateScreenshotHotkeyRegistration();
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

            // Unregister Raw Input
            UnregisterRawInput();

            if (_windowHandle != IntPtr.Zero)
            {
                // F2/F3 now use keyboard hook, only F5/F6 use RegisterHotKey
                if (_devMode)
                {
                    UnregisterHotKey(_windowHandle, F5_HOTKEY_ID);
                    UnregisterHotKey(_windowHandle, F6_HOTKEY_ID);
                }
            }
            _source?.RemoveHook(HandleMessages);
        }

        private void RegisterRawInput()
        {
            if (_rawInputRegistered || _windowHandle == IntPtr.Zero) return;

            var rid = new RAWINPUTDEVICE[]
            {
                new()
                {
                    usUsagePage = 0x01,      // Generic Desktop
                    usUsage = 0x06,          // Keyboard
                    dwFlags = RIDEV_INPUTSINK, // Receive input even when not in foreground
                    hwndTarget = _windowHandle
                }
            };

            if (RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                _rawInputRegistered = true;
                // Pre-allocate buffer for RAWINPUT struct (avoids per-keystroke allocation)
                _rawInputBufferSize = (uint)Marshal.SizeOf<RAWINPUT>() + 16; // Extra padding
                _rawInputBuffer = Marshal.AllocHGlobal((int)_rawInputBufferSize);
                Logger.Log("Raw Input registered for keyboard (zero-lag hotkey detection)");
            }
            else
            {
                Logger.Log($"Failed to register Raw Input: {Marshal.GetLastWin32Error()}");
            }
        }

        private void UnregisterRawInput()
        {
            if (!_rawInputRegistered) return;

            var rid = new RAWINPUTDEVICE[]
            {
                new()
                {
                    usUsagePage = 0x01,
                    usUsage = 0x06,
                    dwFlags = 0x00000001, // RIDEV_REMOVE
                    hwndTarget = IntPtr.Zero
                }
            };

            RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _rawInputRegistered = false;

            if (_rawInputBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawInputBuffer);
                _rawInputBuffer = IntPtr.Zero;
            }
        }

        public void UpdateScreenshotHotkeyRegistration()
        {
            // Raw Input is always registered — just update tracked keys
            if (_windowHandle == IntPtr.Zero) return;
            var settings = AppSettings.Instance;
            _currentScreenshotKey = settings.ScreenshotKey;
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

                // Only update if we have valid data (at least some recognized heroes)
                // This preserves previous known data when detection fails
                if (filteredBlue.Count > 0)
                    _blueTeamAnalysis = filteredBlue;

                if (filteredRed.Count > 0)
                    _redTeamAnalysis = filteredRed;

                if (_blueTeamAnalysis != null)
                    BlueTeamPanel.UpdateCompositions(_blueTeamAnalysis);

                if (_redTeamAnalysis != null)
                    RedTeamPanel.UpdateCompositions(_redTeamAnalysis);

                if (_matchState?.PlayerHero != null && _blueTeamAnalysis != null)
                {
                    SwapSuggestionsPanel.UpdateSuggestions(_matchState.PlayerHero, _blueTeamAnalysis, _matchState.Map);

                    // Show appropriate status indicator
                    // Always mark as updated on successful API response — even if some heroes are Unknown.
                    // The hero panels still fall back to cached data internally, but the indicator
                    // reflects that the server responded successfully.
                    bool hasPersistedSlots = response.PersistedBlueTeamSlots.Count > 0 ||
                                             response.PersistedRedTeamSlots.Count > 0;

                    if (hasPersistedSlots)
                        DataFreshnessIndicator.SetPartialUpdate(
                            response.PersistedBlueTeamSlots.Count + response.PersistedRedTeamSlots.Count);
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

        private void HandleRawInput(IntPtr lParam)
        {
            if (_rawInputBuffer == IntPtr.Zero) return;

            uint dwSize = _rawInputBufferSize;
            if (GetRawInputData(lParam, RID_INPUT, _rawInputBuffer, ref dwSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == unchecked((uint)-1))
                return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(_rawInputBuffer);
            if (raw.header.dwType != RIM_TYPEKEYBOARD) return;

            int vkCode = raw.keyboard.VKey;

            // Fast path: skip keys we don't care about
            if (vkCode != _currentScreenshotKey && vkCode != _currentSwapKey && vkCode != _currentTeamKey)
                return;

            bool isKeyDown = raw.keyboard.Message == WM_KEYDOWN;
            bool isKeyUp = raw.keyboard.Message == WM_KEYUP;
            if (!isKeyDown && !isKeyUp) return;

                // Handle screenshot key
                if (vkCode == _currentScreenshotKey)
                {
                    if (isKeyDown && !_screenshotKeyPressed)
                    {
                        _screenshotKeyPressed = true;
                        _swapVisibilityBeforeScreenshot = SwapLayout.Visibility;
                        _compVisibilityBeforeScreenshot = CompositionLayout.Visibility;
                        _hudWasVisibleBeforeScreenshot =
                            _swapVisibilityBeforeScreenshot == Visibility.Visible ||
                            _compVisibilityBeforeScreenshot == Visibility.Visible;

                        Logger.Log($"Screenshot key pressed (vkCode={vkCode})");
                        CaptureScreenshotWithHiddenHud();
                    }
                    else if (isKeyUp && _screenshotKeyPressed)
                    {
                        _screenshotKeyPressed = false;
                        Logger.Log("Screenshot key released - restoring HUD");
                        if (_hudWasVisibleBeforeScreenshot)
                        {
                            SwapLayout.Visibility = _swapVisibilityBeforeScreenshot;
                            CompositionLayout.Visibility = _compVisibilityBeforeScreenshot;
                            UpdateWindowVisibility();
                        }
                    }
                }

                // Handle swap key (F2) - track held state to prevent flicker from key repeat
                if (vkCode == _currentSwapKey && !KeyHelper.IsMouseButton(_currentSwapKey))
                {
                    if (isKeyDown && !_swapKeyHeld)
                    {
                        _swapKeyHeld = true;
                        ToggleVisibility();
                    }
                    else if (isKeyUp)
                    {
                        _swapKeyHeld = false;
                    }
                }

                // Handle team key (F3) - track held state to prevent flicker from key repeat
                if (vkCode == _currentTeamKey && !KeyHelper.IsMouseButton(_currentTeamKey))
                {
                    if (isKeyDown && !_teamKeyHeld)
                    {
                        _teamKeyHeld = true;
                        ToggleCompositionDashboard();
                    }
                    else if (isKeyUp)
                    {
                        _teamKeyHeld = false;
                    }
                }
        }

        private IntPtr HandleMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_INPUT:
                    HandleRawInput(lParam);
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
