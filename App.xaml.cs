﻿using Hardcodet.Wpf.TaskbarNotification;
using Owcounter.Authentication;
using Owcounter.Display;
using Owcounter.Services;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;

namespace Owcounter
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
        private const string ApiBaseUrl = "https://api.owcounter.com";

#if DEBUG
        public const bool DEV_MODE = true;  // Set this to false to test normal mode while debugging
#else
        public const bool DEV_MODE = false;
#endif

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

#if DEBUG
            AllocConsole();
#endif
            InitializeServices();
            InitializeTrayIcon();
            InitializeApplication();
        }

        private void InitializeServices()
        {
            try
            {
                _keycloakAuth = new KeycloakAuth();
                _apiService = new ApiService(ApiBaseUrl, _keycloakAuth);
                IconUtils.Initialize();
                _overlayWindow = new OverlayWindow(DEV_MODE);
                _overlayWindow.Show();
                _monitoringService = new ScreenshotMonitoringService(_apiService);

                _monitoringService.ScreenshotProcessed += (sender, response) =>
                {
                    if (response != null)
                    {
                        _overlayWindow?.OnScreenshotProcessed(sender ?? this, response);
                    }
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
                    Icon = new System.Drawing.Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OWCounterHUD.ico")),
                    ToolTipText = "OWCOUNTER HUD (F2: Toggle)"
                };

                _notifyIcon.ContextMenu = new System.Windows.Controls.ContextMenu();
                _notifyIcon.ContextMenu.Items.Add(CreateMenuItem("Toggle HUD (F2)", () => _overlayWindow?.ToggleVisibility()));
                var logMenuItem = new System.Windows.Controls.MenuItem { Header = "Open Log" };
                logMenuItem.Click += OpenLog;
                _notifyIcon.ContextMenu.Items.Add(logMenuItem);
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
            if (await _apiService!.LoadAndValidateTokens())
            {
                StartServices();
            }
            else
            {
                ShowLoginWindow();
            }
        }

        private void StartServices()
        {
            try
            {
                _monitoringService?.StartMonitoring();
                UpdateTrayTooltip("OWCOUNTER HUD - Monitoring");
                ShowNotification("OWCOUNTER HUD", "Press F2 or use the tray icon to toggle the hud visibility.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start services: {ex.Message}");
            }
        }

        private void ShowLoginWindow()
        {
            var loginWindow = new LoginWindow(_keycloakAuth!);
            if (loginWindow.ShowDialog() == true)
            {
                _ = Task.Run(async () =>
                {
                    if (await _apiService!.LoadAndValidateTokens())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ShowWelcomeMessage();
                            StartServices();
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("Login failed. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            ShowLoginWindow();
                        });
                    }
                });
            }
            else
            {
                ExitApplication();
            }
        }

        private void ShowWelcomeMessage()
        {
            MessageBox.Show(
                "Welcome to OWCOUNTER HUD!\n\n" +
                "The app is now running in the background.\n" +
                "Press F2 to display the hud on top of your game to see real-time hero analysis and suggestions.",
                "OWCOUNTER HUD",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void OpenLog(object? sender, EventArgs e)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OwcounterHUD.log");
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
                UpdateTrayTooltip("OWCOUNTER HUD - Not logged in");
                ShowNotification("OWCOUNTER HUD", "Logged out successfully.");
                ShowLoginWindow();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during logout: {ex.Message}");
            }
        }

        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            _monitoringService?.Dispose();
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
            base.OnExit(e);
        }
    }
}