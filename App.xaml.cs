using Hardcodet.Wpf.TaskbarNotification;
using Owmeta.Authentication;
using Owmeta.Display;
using Owmeta.Services;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;

namespace Owmeta
{
    [SupportedOSPlatform("windows")]
    public partial class App : Application
    {
#if DEBUG
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AllocConsole();
#endif

        private TaskbarIcon? _notifyIcon;
        private OverlayWindow? _overlayWindow;
        private ApiService? _apiService;
        private KeycloakAuth? _keycloakAuth;
        private ScreenshotMonitoringService? _monitoringService;
        private Mutex? _mutex;
        private bool _mutexOwned;
        private bool _isShowingLogin;
        private const string MutexName = "Global\\OWMETA_HUD_INSTANCE";
        private const string ApiBaseUrl = "https://api.owmeta.io";

#if DEBUG
        public const bool DEV_MODE = true;  // Set this to false to test normal mode while debugging
#else
        public const bool DEV_MODE = false;
#endif

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Parse command-line arguments for settings
            ParseSettingsArgs(e.Args);

            // Check for existing instance
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _mutexOwned = createdNew;

            if (!createdNew)
            {
                MessageBox.Show("OWMETA HUD is already running.\nCheck your system tray for the icon.",
                    "OWMETA HUD",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

#if DEBUG
            AllocConsole();
#endif
            try
            {
                InitializeServices();
                InitializeTrayIcon();
                InitializeApplication();
            }
            catch (Exception ex)
            {
                Logger.Log($"Application startup failed: {ex.Message}");
                MessageBox.Show("Failed to start application. Please check the logs for details.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ExitApplication();
            }
        }

        private void InitializeServices()
        {
            try
            {
                _keycloakAuth = new KeycloakAuth();
                _apiService = new ApiService(ApiBaseUrl, _keycloakAuth);

                // Subscribe to session expiry to show login prompt
                _apiService.SessionExpired += OnSessionExpired;

                IconUtils.Initialize();
                _overlayWindow = new OverlayWindow(DEV_MODE);
                _overlayWindow.Show();
                _monitoringService = new ScreenshotMonitoringService(_apiService);
                _overlayWindow.SetScreenshotService(_monitoringService);

                _monitoringService.ScreenshotProcessed += (sender, response) =>
                {
                    if (response != null)
                    {
                        _overlayWindow?.OnScreenshotProcessed(sender ?? this, response);
                    }
                };

                _monitoringService.AnalysisStarted += (sender, e) =>
                {
                    _overlayWindow?.OnAnalysisStarted();
                };

                _monitoringService.AnalysisError += (sender, message) =>
                {
                    _overlayWindow?.OnAnalysisError(message);
                };
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize services: {ex.Message}");
                MessageBox.Show("Failed to initialize application. Please check the logs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ExitApplication();
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new TaskbarIcon
                {
                    Icon = new System.Drawing.Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OWMetaHUD.ico")),
                    ToolTipText = "OWMETA HUD (F2/F3: Toggle)",
                    ContextMenu = new System.Windows.Controls.ContextMenu()
                };
                _notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Swap Suggestions (F2)", () => _overlayWindow?.ToggleVisibility()));
                _notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Team Composition (F3)", () => _overlayWindow?.ToggleCompositionDashboard()));
                var logMenuItem = new System.Windows.Controls.MenuItem { Header = "Open Log" };
                logMenuItem.Click += OpenLog;
                _notifyIcon.ContextMenu.Items.Add(logMenuItem);
                _notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Settings", () => ShowSettingsWindow()));
                _notifyIcon.ContextMenu.Items.Add(new System.Windows.Controls.Separator());
                _notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Logout", () => Logout()));
                _notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Exit", () => ExitApplication()));
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize tray icon: {ex.Message}");
                MessageBox.Show("Failed to initialize system tray icon.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private System.Windows.Controls.MenuItem CreateMenuItem(string header, Action action)
        {
            var menuItem = new System.Windows.Controls.MenuItem { Header = header };
            menuItem.Click += (s, e) => action();
            return menuItem;
        }

        private async void InitializeApplication()
        {
            Logger.Log("[DEV] InitializeApplication - checking tokens...");
            if (await _apiService!.LoadAndValidateTokens())
            {
                Logger.Log("[DEV] Tokens valid - starting services");
                StartServices();
            }
            else
            {
                Logger.Log("[DEV] Tokens invalid - showing login");
                ShowLoginWindow();
            }
        }

        private void StartServices()
        {
            try
            {
                _monitoringService?.StartMonitoring();
                if (!DEV_MODE)
                {
                    UpdateTrayTooltip("OWMETA HUD - Monitoring");
                    ShowNotification("OWMETA HUD", "Press F2 or use the tray icon to toggle the hud visibility.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start services: {ex.Message}");
            }
        }

        private void ShowLoginWindow()
        {
            if (_isShowingLogin) return;
            _isShowingLogin = true;

            var loginWindow = new LoginWindow(_keycloakAuth!);
            if (loginWindow.ShowDialog() == true)
            {
                _ = Task.Run(async () =>
                {
                    if (await _apiService!.LoadAndValidateTokens())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isShowingLogin = false;
                            ShowWelcomeMessage();
                            StartServices();
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isShowingLogin = false;
                            MessageBox.Show("Login failed. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            ShowLoginWindow();
                        });
                    }
                });
            }
            else
            {
                _isShowingLogin = false;
                ExitApplication();
            }
        }

        private void ShowWelcomeMessage()
        {
            var disclaimerMessage =
                "Welcome to OWMETA HUD!\n\n" +
                "Important Notice:\n" +
                "The hero recommendations and analysis provided by this tool are meant to help you learn and understand the game better. " +
                "They are based on general strategic principles and community knowledge, but should not be taken as absolute rules.\n\n" +
                "Remember:\n" +
                "• Every match is unique and context-dependent\n" +
                "• Personal skill and experience with heroes matter more than theoretical counters\n" +
                "• The suggestions are learning tools, not guaranteed winning strategies\n" +
                "• Use this information to enhance your game understanding, not as strict rules to follow\n\n" +
                "The app is now running in the background.\n" +
                "Press F2 to display the HUD on top of your game to see real-time hero analysis and suggestions.";

            MessageBox.Show(
                disclaimerMessage,
                "OWMETA HUD - Getting Started",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void ShowSettingsWindow()
        {
            var settingsWindow = new SettingsWindow();
            if (settingsWindow.ShowDialog() == true)
            {
                _overlayWindow?.RefreshDisplay();
            }
        }

        private void OpenLog(object? sender, EventArgs e)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OwmetaHUD.log");
                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("Log file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error opening log file: {ex.Message}");
                MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Logout()
        {
            try
            {
                await _apiService!.Logout();
                _monitoringService?.Dispose();
                UpdateTrayTooltip("OWMETA HUD - Not logged in");
                ShowNotification("OWMETA HUD", "Logged out successfully.");
                ShowLoginWindow();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during logout: {ex.Message}");
            }
        }

        private void OnSessionExpired(object? sender, EventArgs e)
        {
            Logger.Log("Session expired - prompting for re-login");
            Dispatcher.Invoke(() =>
            {
                UpdateTrayTooltip("OWMETA HUD - Session expired");
                ShowNotification("OWMETA HUD", "Your session has expired. Please login again.");
                ShowLoginWindow();
            });
        }

        private void ExitApplication()
        {
            _monitoringService?.Dispose();
            if (_apiService != null)
            {
                _apiService.SessionExpired -= OnSessionExpired;
                _apiService.Dispose();
            }
            Current.Shutdown();
        }

        private void UpdateTrayTooltip(string message)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.ToolTipText = message;
            }
        }

        private void ShowNotification(string title, string message)
        {
            _notifyIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            _apiService?.Dispose();
            if (_mutexOwned)
            {
                _mutex?.ReleaseMutex();
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void ParseSettingsArgs(string[] args)
        {
            foreach (var arg in args)
            {
                if (arg.StartsWith("--min-score-f2=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(arg.Substring("--min-score-f2=".Length), out int value))
                    {
                        AppSettings.Instance.MinScoreF2 = value;
                        AppSettings.Instance.Save();
                    }
                }
                else if (arg.StartsWith("--min-score-f3=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(arg.Substring("--min-score-f3=".Length), out int value))
                    {
                        AppSettings.Instance.MinScoreF3 = value;
                        AppSettings.Instance.Save();
                    }
                }
            }
        }
    }
}