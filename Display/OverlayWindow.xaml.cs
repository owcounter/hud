using Microsoft.Win32;
using Owmeta.Model;
using Owmeta.Services;
using System.Diagnostics;
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
        private const int WM_SHOWWINDOW = 0x0018;

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
        private HeroName? _displayedPlayerHero;
        private readonly MatchBoundaryTracker _matchBoundaryTracker = new();
        private Dictionary<HeroName, HeroAnalysis>? _playerAnalyses;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetMessageTime();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

        private const string OVERWATCH_WINDOW_TITLE = "Overwatch";
        private const string OVERWATCH_CLASS_NAME = "TankWindowClass";
        private readonly DispatcherTimer _positionTimer;
        private HwndSource? _source;
        private IntPtr _windowHandle;
        private readonly bool _devMode;
        private ScreenshotMonitoringService? _screenshotService;
        private bool _boundsRefreshPending;
        private bool _closed;
        private bool _surfaceVisible;
        private bool _dashboardUpdatePending;
        private DispatcherTimer? _parkTimer;
        private bool _parked;
        private DispatcherTimer? _diagnosticTimer;
        private long _diagnosticInputSequence;

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
        private readonly OverlayContentState _contentState = new();
        private CancellationTokenSource? _captureCancellation;
        private bool _screenshotKeyPressed;

        // Track hotkey pressed state to prevent flickering from key repeat
        private bool _swapKeyHeld;
        private bool _teamKeyHeld;

        public OverlayWindow(bool devMode)
        {
            _devMode = devMode;

            InitializeComponent();
            SwapContent.SizeChanged += (_, _) => SyncContentHeight();
            CompositionDashboard.SizeChanged += (_, _) => SyncContentHeight();
            NoDataPanel.SizeChanged += (_, _) => SyncContentHeight();
            InitializeWindow();
            UpdateHotkeyLabels();
            UpdateDataState();
            UpdateHeaderTabs();

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
            QueueBoundsRefresh();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            // Let WPF process the DPI change before restoring the game's pixel bounds.
            QueueBoundsRefresh();
        }

        private void QueueBoundsRefresh()
        {
            if (_closed || Dispatcher.HasShutdownStarted) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(QueueBoundsRefresh));
                return;
            }
            if (_boundsRefreshPending) return;
            _boundsRefreshPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                _boundsRefreshPending = false;
                if (_closed || _windowHandle == IntPtr.Zero) return;
                SetToScreenResolution();
                OnPositionCheck(this, EventArgs.Empty);
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
            ApplyRenderMode();

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

            // Also align the initially hidden overlay before its first hotkey toggle.
            SetToScreenResolution();

            // Content toggles drive visibility; the HWND itself is parked (natively hidden)
            // only after an empty frame was presented. See UnparkWindow/SchedulePark.
            _contentState.GameAvailable = _devMode;
            UpdateWindowVisibility();
            _positionTimer.Start();
            _diagnosticTimer = HudDiagnostics.StartHeartbeat(Dispatcher);
            TraceHudState("loaded");
        }

        // A hardware-rendered per-pixel-alpha window is read back from the GPU on every
        // frame before DWM sees it; that readback queues behind a GPU-bound game (measured
        // 0.25-2.2 s, frames dropped). Software rendering keeps the overlay off the game's
        // GPU queue. OWMETA_HUD_RENDER_MODE=hardware remains for A/B diagnostics only.
        private void ApplyRenderMode()
        {
            bool software = Environment.GetEnvironmentVariable("OWMETA_HUD_RENDER_MODE") != "hardware";
            if (software && _source?.CompositionTarget != null)
            {
                _source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }
            Logger.Log($"Overlay render mode: {(software ? "software" : "hardware")}, tier={System.Windows.Media.RenderCapability.Tier >> 16}");
            HudDiagnostics.Write($"render-mode software={software} tier={System.Windows.Media.RenderCapability.Tier >> 16}");
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
            UpdateHotkeyLabels();
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

        // Enumerates every process (10-20 ms on the UI thread): callers only use it when
        // the cheap window lookup fails.
        private static bool IsOverwatchProcessRunning()
        {
            return Process.GetProcessesByName("Overwatch").Any();
        }

        private void OnPositionCheck(object? sender, EventArgs e)
        {
            using var diagnostic = HudDiagnostics.Measure("position-check", 30);
            var overwatchHandle = FindOverwatchWindow();
            bool overwatchRunning = overwatchHandle != IntPtr.Zero || IsOverwatchProcessRunning();
            bool overwatchWindowFound = overwatchHandle != IntPtr.Zero;

            _contentState.GameAvailable = OverwatchPresencePolicy.CanShowOverlay(_devMode, overwatchRunning, overwatchWindowFound);
            UpdateWindowVisibility();
            if (overwatchWindowFound && (_contentState.ShowSwap || _contentState.ShowComposition))
            {
                PositionOverOverwatch(overwatchHandle);
            }
        }

        private void PositionOverOverwatch(IntPtr overwatchHandle)
        {
            using var diagnostic = HudDiagnostics.Measure("position-apply", 5);
            // A minimized/invalid target has no usable client rectangle; keep the last
            // bounds until it returns instead of moving to Windows' minimized coordinates.
            OverlayPlacement.TryFollowWindow(_windowHandle, overwatchHandle);
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _captureCancellation?.Cancel();
            _captureCancellation?.Dispose();
            _diagnosticTimer?.Stop();
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
                if (_windowHandle == IntPtr.Zero) return;
                var overwatchHandle = FindOverwatchWindow();
                if (overwatchHandle != IntPtr.Zero)
                {
                    PositionOverOverwatch(overwatchHandle);
                }
                else
                {
                    var primaryScreen = Screen.PrimaryScreen;
                    if (primaryScreen != null)
                    {
                        OverlayPlacement.TrySetBounds(_windowHandle, primaryScreen.Bounds);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error setting screen resolution: {ex.Message}");
            }
        }

        public void OnScreenshotProcessed(object sender, ScreenshotProcessingResponse response)
        {
            Dispatcher.Invoke(() =>
            {
                using var diagnostic = HudDiagnostics.Measure("apply-analysis-ui");
                // Filter out Unknown/Hidden heroes, keeping only recognized ones
                var filteredBlue = FilterUnknownHeroes(response.BlueTeamAnalysis);
                var filteredRed = FilterUnknownHeroes(response.RedTeamAnalysis);

                bool knownMapChanged = _matchBoundaryTracker.Observe(response.MatchState.Map);
                if (knownMapChanged)
                {
                    ResetAnalysisState($"map changed to {response.MatchState.Map}");
                }

                _matchState = response.MatchState;

                // The backend marks historical values explicitly; raw worker evidence
                // stays separate and is never overwritten by this display state.
                _blueTeamAnalysis = filteredBlue;
                _redTeamAnalysis = filteredRed;

                Logger.Log(
                    $"HUD response: map={response.MatchState.Map}, player={response.MatchState.PlayerHero}, blueRecognized={filteredBlue.Count}, redRecognized={filteredRed.Count}, persistedRed={response.PersistedRedTeamSlots.Count}, knownMapChanged={knownMapChanged}");

                if (_blueTeamAnalysis != null)
                {
                    _playerAnalyses = ServerRecognitionContract.PlayerAnalyses(response);
                    var selectedHero = SelectedHeroPolicy.ResolveForSwapPanel(response.MatchState.PlayerHero, null, _playerAnalyses);

                    response.Capture?.Write("displayed.json", new {
                        map = (int)response.MatchState.Map,
                        observedPlayer = response.RawRecognition?.PlayerHero,
                        displayedPlayer = selectedHero.Hero.HasValue ? (int?)selectedHero.Hero.Value : null,
                        UsedPreviousHero = response.PersistedPlayerHero, selectedHero.Reason,
                        blueHeroes = _blueTeamAnalysis.Keys.Select(h => (int)h).ToArray(),
                        redHeroes = _redTeamAnalysis?.Keys.Select(h => (int)h).ToArray(),
                        response.PersistedBlueTeamSlots, response.PersistedRedTeamSlots, response.PersistedPlayerHero, response.RecognitionFailed,
                        captureWarning = response.Capture.Warning
                    });

                    if (selectedHero.Hero.HasValue)
                    {
                        if (response.PersistedPlayerHero)
                        {
                            Logger.Log($"F2 using previous selected hero {selectedHero.Hero.Value}: {selectedHero.Reason}");
                        }

                        _displayedPlayerHero = selectedHero.Hero.Value;
                        SwapSuggestionsPanel.UpdateSuggestions(selectedHero.Hero.Value, _playerAnalyses, _redTeamAnalysis);
                    }
                    else
                    {
                        _displayedPlayerHero = null;
                        SwapSuggestionsPanel.UpdateSuggestions(HeroName.Unknown, _playerAnalyses, _redTeamAnalysis,
                            "HERO NOT IDENTIFIED · OPEN THE SCOREBOARD AGAIN");
                        Logger.Log($"F2 cleared: {selectedHero.Reason}");
                    }

                    // Show appropriate status indicator
                    // Always mark as updated on successful API response — even if some heroes are Unknown.
                    // The hero panels still fall back to cached data internally, but the indicator
                    // reflects that the server responded successfully.
                    int persistedSlotCount = response.PersistedBlueTeamSlots.Count +
                                             response.PersistedRedTeamSlots.Count;
                    bool hasPersistedSlots = persistedSlotCount > 0;

                    // A wholly rejected frame keeps the previous data age; partial successes are fresh data.
                    if (response.RecognitionFailed)
                        DataFreshnessIndicator.SetStatus("Screenshot unreadable; using previous results");
                    else if (response.Capture?.Warning != null)
                        DataFreshnessIndicator.MarkUpdated("capture recording failed - check log");
                    else if (response.PersistedPlayerHero)
                        DataFreshnessIndicator.MarkUpdated("previous player hero");
                    else if (response.RawRecognition?.Slots.Any(s => s.Hero == 1) == true)
                        DataFreshnessIndicator.MarkUpdated("some portraits unreadable");
                    else if (hasPersistedSlots)
                        DataFreshnessIndicator.SetPartialUpdate(persistedSlotCount);
                    else
                        DataFreshnessIndicator.MarkUpdated();
                }

                // Update composition dashboard (F3 layout)
                ScheduleCompositionDashboardUpdate();
                UpdateMapInfo();
                UpdateDataState();

                if (knownMapChanged && filteredBlue.Count == 0 && filteredRed.Count == 0)
                {
                    DataFreshnessIndicator.SetStatus("New match detected - capture again");
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
                // Settings changes (grouping, min score) rebuild the view for the hero already shown.
                if (_displayedPlayerHero.HasValue && _playerAnalyses != null)
                {
                    SwapSuggestionsPanel.UpdateSuggestions(_displayedPlayerHero.Value, _playerAnalyses, _redTeamAnalysis);
                }

                UpdateCompositionDashboard();
                UpdateCurrentHeroPlate();

                // Update hotkey registrations based on current settings
                UpdateHotkeyRegistrations();
                UpdateScreenshotHotkeyRegistration();
            });
        }

        // Rebuilding the hidden F3 dashboard costs ~80 ms of layout; run it below input
        // priority so an F2 pressed right after an analysis is not queued behind it.
        private void ScheduleCompositionDashboardUpdate()
        {
            if (CompositionLayout.Visibility == Visibility.Visible)
            {
                UpdateCompositionDashboard();
                return;
            }
            if (_dashboardUpdatePending) return;
            _dashboardUpdatePending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushCompositionDashboardUpdate));
        }

        private void FlushCompositionDashboardUpdate()
        {
            if (!_dashboardUpdatePending) return;
            _dashboardUpdatePending = false;
            UpdateCompositionDashboard();
        }

        private void UpdateCompositionDashboard()
        {
            if (_blueTeamAnalysis == null)
            {
                return;
            }

            CompositionDashboard.Update(
                _blueTeamAnalysis,
                _redTeamAnalysis ?? new Dictionary<HeroName, HeroAnalysis>(),
                _matchState?.Map);
        }

        private void ResetAnalysisState(string reason)
        {
            _matchState = null;
            _blueTeamAnalysis = null;
            _redTeamAnalysis = null;
            _playerAnalyses = null;
            _displayedPlayerHero = null;

            SwapSuggestionsPanel.Clear();
            CompositionDashboard.Clear();
            DataFreshnessIndicator.Reset();
            UpdateMapInfo();
            UpdateDataState();

            Logger.Log($"HUD analysis cache reset: {reason}");
        }

        private void UpdateMapInfo()
        {
            var map = _matchState?.Map;
            bool known = map.HasValue && map.Value != MapName.MapUnspecified && map.Value != MapName.AllMaps;
            MapNameText.Text = known ? HudFormat.MapName(map!.Value) : string.Empty;
            MapInfo.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        }

        // Until the first analysis with heroes arrives, both layouts show the same "how to get data" panel.
        private void UpdateDataState()
        {
            bool hasData = _blueTeamAnalysis is { Count: > 0 } || _redTeamAnalysis is { Count: > 0 };
            SwapContent.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            CompositionDashboard.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            NoDataPanel.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            UpdateCurrentHeroPlate();
            SyncContentHeight();
        }

        private void SyncContentHeight()
        {
            FrameworkElement? active =
                NoDataPanel.Visibility == Visibility.Visible ? NoDataPanel
                : SwapLayout.Visibility == Visibility.Visible ? SwapContent
                : CompositionLayout.Visibility == Visibility.Visible ? CompositionDashboard
                : null;
            if (active == null) return;
            double height = active.ActualHeight > 0 ? active.ActualHeight : active.DesiredSize.Height;
            if (height > 0) ContentHost.Height = height;
        }

        // Your hero and score sit next to the tabs while the swap view is open.
        private void UpdateCurrentHeroPlate()
        {
            var current = SwapSuggestionsPanel.CurrentHero;
            bool show = current.HasValue
                        && SwapLayout.Visibility == Visibility.Visible
                        && NoDataPanel.Visibility != Visibility.Visible;
            CurrentHeroPlate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            CurrentHeroAvatar.Source = Board.Portrait(current!.Value.Hero);
            CurrentHeroName.Text = HudFormat.HeroName(current.Value.Hero).ToUpperInvariant();
            CurrentHeroScore.Text = HudFormat.Score(current.Value.Score);
            CurrentHeroScore.Foreground = HudPalette.ScoreBrush(current.Value.Score);
        }

        private void UpdateHotkeyLabels()
        {
            var settings = AppSettings.Instance;
            var swapKey = KeyHelper.GetKeyName(settings.SwapSuggestionsKey);
            var teamKey = KeyHelper.GetKeyName(settings.TeamCompositionKey);

            SwapKeyText.Text = swapKey;
            TeamKeyText.Text = teamKey;
            HintSwapKeyText.Text = swapKey;
            HintTeamKeyText.Text = teamKey;

            if (settings.TabScreenshotEnabled)
            {
                CaptureKeyText.Text = KeyHelper.GetKeyName(settings.ScreenshotKey);
                CaptureHint.Visibility = Visibility.Visible;
                NoDataTitle.Text = "Open the scoreboard to analyze this match";
            }
            else
            {
                CaptureHint.Visibility = Visibility.Collapsed;
                NoDataTitle.Text = "Take a scoreboard screenshot to analyze this match";
            }
        }

        // The header and "no data" panel sit outside the F2/F3 layouts, so the whole HUD
        // surface is shown only while one of the layouts is active (and never during capture).
        private void UpdateHeaderTabs()
        {
            bool swapActive = SwapLayout.Visibility == Visibility.Visible;
            bool teamActive = CompositionLayout.Visibility == Visibility.Visible;
            bool surfaceVisible = swapActive || teamActive;
            MainGrid.Visibility = surfaceVisible ? Visibility.Visible : Visibility.Hidden;
            SetTabState(SwapTab, SwapTabText, swapActive);
            SetTabState(TeamTab, TeamTabText, teamActive);
            UpdateCurrentHeroPlate();
            SyncContentHeight();
            if (surfaceVisible != _surfaceVisible)
            {
                _surfaceVisible = surfaceVisible;
                TraceSurfaceChange(surfaceVisible);
            }
        }

        // Pairs with the external probe, which timestamps when the marker actually reaches the screen.
        private void TraceSurfaceChange(bool visible)
        {
            if (!HudDiagnostics.Enabled) return;
            PresentProbeMarker.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            HudDiagnostics.Write($"hud-surface visible={visible} inputSeq={_diagnosticInputSequence}");
            EventHandler? onFrame = null;
            onFrame = (_, _) =>
            {
                System.Windows.Media.CompositionTarget.Rendering -= onFrame;
                HudDiagnostics.Write($"wpf-frame visible={visible} inputSeq={_diagnosticInputSequence}");
            };
            System.Windows.Media.CompositionTarget.Rendering += onFrame;
        }

        private static void SetTabState(System.Windows.Controls.Border tab, System.Windows.Controls.TextBlock label, bool active)
        {
            tab.Background = active ? HudPalette.TabActive : HudPalette.TabIdle;
            label.Foreground = active ? HudPalette.TabActiveText : HudPalette.TabIdleText;
        }

        public void ToggleVisibility()
        {
            using var diagnostic = HudDiagnostics.Measure("toggle-f2-total");
            TraceHudState("f2-begin");
            var presenceStart = Stopwatch.GetTimestamp();
            var overwatchHandle = FindOverwatchWindow();
            bool overwatchRunning = overwatchHandle != IntPtr.Zero || IsOverwatchProcessRunning();
            bool overwatchWindowFound = overwatchHandle != IntPtr.Zero;
            HudDiagnostics.Write($"f2-presence elapsedMs={Stopwatch.GetElapsedTime(presenceStart).TotalMilliseconds:F2} running={overwatchRunning} found={overwatchWindowFound}");

            if (!OverwatchPresencePolicy.CanShowOverlay(_devMode, overwatchRunning, overwatchWindowFound))
            {
                Logger.Log("F2 ignored: Overwatch is not running");
                return;
            }

            _contentState.GameAvailable = true;
            _contentState.Toggle(1);

            UpdateWindowVisibility();

            Logger.Log($"F2 toggled: swap={SwapLayout.Visibility}, composition={CompositionLayout.Visibility}, processRunning={overwatchRunning}, windowFound={overwatchWindowFound}");

            if (IsVisible && overwatchWindowFound)
            {
                PositionOverOverwatch(overwatchHandle);
            }
            else if (IsVisible)
            {
                SetToScreenResolution();
            }
            TraceHudState("f2-end");
        }

        public void ToggleCompositionDashboard()
        {
            using var diagnostic = HudDiagnostics.Measure("toggle-f3-total");
            TraceHudState("f3-begin");
            var overwatchHandle = FindOverwatchWindow();
            bool overwatchRunning = overwatchHandle != IntPtr.Zero || IsOverwatchProcessRunning();
            bool overwatchWindowFound = overwatchHandle != IntPtr.Zero;

            if (!OverwatchPresencePolicy.CanShowOverlay(_devMode, overwatchRunning, overwatchWindowFound))
            {
                Logger.Log("F3 ignored: Overwatch is not running");
                return;
            }

            FlushCompositionDashboardUpdate();
            _contentState.GameAvailable = true;
            _contentState.Toggle(2);

            UpdateWindowVisibility();

            Logger.Log($"F3 toggled: swap={SwapLayout.Visibility}, composition={CompositionLayout.Visibility}, processRunning={overwatchRunning}, windowFound={overwatchWindowFound}");

            if (IsVisible && overwatchWindowFound)
            {
                PositionOverOverwatch(overwatchHandle);
            }
            else if (IsVisible)
            {
                SetToScreenResolution();
            }
            TraceHudState("f3-end");
        }

        private void TraceHudState(string reason)
        {
            if (!HudDiagnostics.Enabled) return;
            HudDiagnostics.Write($"hud-state reason={reason} inputSeq={_diagnosticInputSequence} window={Visibility} isVisible={IsVisible} nativeVisible={IsWindowVisible(_windowHandle)} swap={SwapLayout.Visibility} composition={CompositionLayout.Visibility} tabHeld={_screenshotKeyPressed} f2Held={_swapKeyHeld} f3Held={_teamKeyHeld} foreground=0x{GetForegroundWindow().ToInt64():X}");
        }

        private void UpdateWindowVisibility()
        {
            using var diagnostic = HudDiagnostics.Measure("set-window-visibility");
            TraceHudState("visibility-before");
            bool surfaceVisible = _contentState.ShowSwap || _contentState.ShowComposition;
            if (surfaceVisible) UnparkWindow();
            SwapLayout.Visibility = _contentState.ShowSwap ? Visibility.Visible : Visibility.Hidden;
            CompositionLayout.Visibility = _contentState.ShowComposition ? Visibility.Visible : Visibility.Hidden;
            UpdateHeaderTabs();
            if (!surfaceVisible) SchedulePark();
            TraceHudState("visibility-after");
            if (HudDiagnostics.Enabled)
            {
                var sequence = _diagnosticInputSequence;
                var queuedAt = Stopwatch.GetTimestamp();
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (_closed) return;
                    HudDiagnostics.Write($"visibility-dispatcher-idle originInputSeq={sequence} elapsedMs={Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds:F2}");
                    TraceHudState("visibility-idle");
                }));
            }
        }

        // A shown full-screen layered window, even fully transparent, forces DWM to compose
        // every game frame. Hide the HWND once its transparent frame has been presented, and
        // reshow it (still transparent) before content becomes visible, so no stale frame flashes.
        private void UnparkWindow()
        {
            _parkTimer?.Stop();
            if (!_parked || _windowHandle == IntPtr.Zero) return;
            _parked = false;
            ShowWindow(_windowHandle, SW_SHOWNOACTIVATE);
            SetWindowPos(_windowHandle, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
            HudDiagnostics.Write($"native-unpark inputSeq={_diagnosticInputSequence}");
        }

        private void SchedulePark()
        {
            if (_parked || _windowHandle == IntPtr.Zero) return;
            if (_parkTimer == null)
            {
                _parkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _parkTimer.Tick += (_, _) =>
                {
                    _parkTimer.Stop();
                    if (_closed || _parked || _contentState.ShowSwap || _contentState.ShowComposition) return;
                    _parked = true;
                    ShowWindow(_windowHandle, SW_HIDE);
                    HudDiagnostics.Write($"native-park inputSeq={_diagnosticInputSequence}");
                };
            }
            // Not restarted by the periodic position check, only started once per hide.
            if (!_parkTimer.IsEnabled) _parkTimer.Start();
        }

        private void CaptureScreenshotWithHiddenHud()
        {
            TraceHudState("tab-capture-begin");
            _contentState.Capturing = true;
            UpdateWindowVisibility();
            _captureCancellation?.Dispose();
            _captureCancellation = new CancellationTokenSource();
            _screenshotService?.CaptureAndProcessScreenshot(_captureCancellation.Token, _currentScreenshotKey);
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
            if (HudDiagnostics.Enabled)
            {
                _diagnosticInputSequence++;
                var queueAge = unchecked((uint)Environment.TickCount - (uint)GetMessageTime());
                HudDiagnostics.Write($"hotkey seq={_diagnosticInputSequence} vk={vkCode} down={isKeyDown} queueAgeMs={queueAge} device=0x{raw.header.hDevice.ToInt64():X} flags={raw.keyboard.Flags} tabHeld={_screenshotKeyPressed} f2Held={_swapKeyHeld} f3Held={_teamKeyHeld}");
            }

                // Handle screenshot key
                if (vkCode == _currentScreenshotKey)
                {
                    if (isKeyDown && !_screenshotKeyPressed)
                    {
                        _screenshotKeyPressed = true;

                        Logger.Log($"Screenshot key pressed (vkCode={vkCode})");
                        CaptureScreenshotWithHiddenHud();
                    }
                    else if (isKeyUp && _screenshotKeyPressed)
                    {
                        _screenshotKeyPressed = false;
                        Logger.Log("Screenshot key released - restoring HUD");
                        _captureCancellation?.Cancel();
                        _contentState.Capturing = false;
                        UpdateWindowVisibility();
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
                case WM_SHOWWINDOW:
                    if (HudDiagnostics.Enabled)
                        HudDiagnostics.Write($"native-showwindow show={wParam != IntPtr.Zero} reason={lParam.ToInt64()} inputSeq={_diagnosticInputSequence}");
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
                    break;
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
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
