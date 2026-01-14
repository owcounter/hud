using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Owmeta.Services
{
    public class ScreenshotMonitoringService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();
        private readonly ApiService apiService;
        private readonly SynchronizationContext synchronizationContext;
        public event EventHandler<Model.ScreenshotProcessingResponse>? ScreenshotProcessed;
        public event EventHandler? AnalysisStarted;
        public event EventHandler<string>? AnalysisError;

        // Dev mode navigation
        private List<string> _allScreenshots = new();
        private int _currentScreenshotIndex = 0;
        private string _screenshotsPath = "";

        public ScreenshotMonitoringService(ApiService apiService)
        {
            this.apiService = apiService;
            synchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }
        public void StartMonitoring()
        {
            Logger.Log("[DEV] StartMonitoring called");
            // Monitor Battle.net path - check both possible locations
            string bnetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Overwatch", "ScreenShots", "Overwatch");
            string bnetPathAlt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Overwatch", "ScreenShots");

            if (Directory.Exists(bnetPath))
            {
                AddWatcher(bnetPath);
            }
            else if (Directory.Exists(bnetPathAlt))
            {
                AddWatcher(bnetPathAlt);
                bnetPath = bnetPathAlt;
            }
            else
            {
                Logger.Log($"Battle.net screenshots folder not found: {bnetPath}");
            }

            // Monitor Steam path
            string? steamPath = GetSteamScreenshotPath();
            if (steamPath != null && Directory.Exists(steamPath))
            {
                AddWatcher(steamPath);
            }
            else if (steamPath != null)
            {
                Logger.Log($"Steam screenshots folder not found: {steamPath}");
            }

            // In dev mode, process the latest file immediately
            if (App.DEV_MODE)
            {
                ProcessLatestScreenshotForDev(bnetPath);
            }
        }

        private void ProcessLatestScreenshotForDev(string bnetPath)
        {
            // First check for dev-screenshots folder in app directory (easiest for testing)
            string devScreenshotsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dev-screenshots");

            string? latestFile = null;

            // Priority 1: dev-screenshots folder in app directory
            if (Directory.Exists(devScreenshotsPath))
            {
                _screenshotsPath = devScreenshotsPath;
                LoadAllScreenshots(devScreenshotsPath);
                latestFile = _allScreenshots.FirstOrDefault();
                if (latestFile != null)
                {
                    Logger.Log($"[DEV] Found {_allScreenshots.Count} screenshots in dev-screenshots folder");
                }
            }

            // Priority 2: Overwatch screenshots folder
            if (latestFile == null && Directory.Exists(bnetPath))
            {
                _screenshotsPath = bnetPath;
                LoadAllScreenshots(bnetPath);
                latestFile = _allScreenshots.FirstOrDefault();
                if (latestFile != null)
                {
                    Logger.Log($"[DEV] Found {_allScreenshots.Count} screenshots in Overwatch folder");
                }
            }

            if (latestFile != null)
            {
                _currentScreenshotIndex = 0;
                Logger.Log($"[DEV] Auto-processing screenshot: {Path.GetFileName(latestFile)}");
                Logger.Log($"[DEV] Use F5/F6 to navigate between screenshots");
                var directory = Path.GetDirectoryName(latestFile);
                if (directory != null)
                {
                    // Small delay to ensure UI is ready
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        synchronizationContext.Post(__ =>
                        {
                            OnFileCreated(this, new FileSystemEventArgs(WatcherChangeTypes.Created, directory, Path.GetFileName(latestFile)));
                        }, null);
                    });
                }
            }
            else
            {
                Logger.Log($"[DEV] No screenshots found. To test:");
                Logger.Log($"[DEV]   1. Create folder: {devScreenshotsPath}");
                Logger.Log($"[DEV]   2. Put an Overwatch Tab screen screenshot (.jpg or .png) in it");
                Logger.Log($"[DEV]   OR use screenshots from: {bnetPath}");
            }
        }

        private void LoadAllScreenshots(string path)
        {
            _allScreenshots = Directory.GetFiles(path, "*.jpg", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(path, "*.png", SearchOption.AllDirectories))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
        }

        public void NextScreenshot()
        {
            if (!App.DEV_MODE || _allScreenshots.Count == 0) return;

            _currentScreenshotIndex = (_currentScreenshotIndex + 1) % _allScreenshots.Count;
            ProcessScreenshotAtIndex(_currentScreenshotIndex);
        }

        public void PreviousScreenshot()
        {
            if (!App.DEV_MODE || _allScreenshots.Count == 0) return;

            _currentScreenshotIndex = (_currentScreenshotIndex - 1 + _allScreenshots.Count) % _allScreenshots.Count;
            ProcessScreenshotAtIndex(_currentScreenshotIndex);
        }

        public void CaptureAndProcessScreenshot()
        {
            synchronizationContext.Post(async _ =>
            {
                // Check if Overwatch is running (unless in dev mode)
                var overwatchProcess = Process.GetProcessesByName("Overwatch").FirstOrDefault();
                if (!App.DEV_MODE && overwatchProcess == null)
                {
                    Logger.Log("TAB pressed but Overwatch is not running - ignoring");
                    return;
                }

                // Wait for the scoreboard to appear after TAB is pressed
                await Task.Delay(AppSettings.Instance.ScreenshotDelayMs);

                try
                {
                    // Capture the screen where Overwatch is running
                    Screen? screen = null;
                    if (overwatchProcess != null && overwatchProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        screen = Screen.FromHandle(overwatchProcess.MainWindowHandle);
                    }
                    screen ??= Screen.PrimaryScreen;

                    if (screen == null)
                    {
                        Logger.Log("Could not get screen for capture");
                        return;
                    }

                    Logger.Log($"Capturing screen: {screen.DeviceName} ({screen.Bounds.Width}x{screen.Bounds.Height})");

                    var bounds = screen.Bounds;
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    }

                    // Notify that analysis is starting
                    AnalysisStarted?.Invoke(this, EventArgs.Empty);

                    // Convert to base64 directly in memory (no temp file needed)
                    using var ms = new MemoryStream();
                    bitmap.Save(ms, ImageFormat.Jpeg);
                    var imageBytes = ms.ToArray();
                    var base64String = Convert.ToBase64String(imageBytes);

                    Logger.Log($"Screenshot captured ({imageBytes.Length} bytes)");

                    var response = await apiService.SendScreenshotToServer(base64String);
                    if (response != null)
                    {
                        ScreenshotProcessed?.Invoke(this, response);
                    }
                    else
                    {
                        Logger.Log("Received null response from screenshot processing");
                        AnalysisError?.Invoke(this, "No response from server");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error capturing/processing screenshot: {ex.Message}");
                    AnalysisError?.Invoke(this, "Error capturing screenshot");
                }
            }, null);
        }

        private void ProcessScreenshotAtIndex(int index)
        {
            if (index < 0 || index >= _allScreenshots.Count) return;

            var file = _allScreenshots[index];
            var directory = Path.GetDirectoryName(file);
            if (directory != null)
            {
                Logger.Log($"[DEV] Processing screenshot {index + 1}/{_allScreenshots.Count}: {Path.GetFileName(file)}");
                OnFileCreated(this, new FileSystemEventArgs(WatcherChangeTypes.Created, directory, Path.GetFileName(file)));
            }
        }

        private string? FindLatestScreenshot(string path, int skip = 0)
        {
            return Directory.GetFiles(path, "*.jpg", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(path, "*.png", SearchOption.AllDirectories))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Skip(skip)
                .FirstOrDefault();
        }

        private string? GetSteamScreenshotPath()
        {
            try
            {
                string steamPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Steam"
                );

                string? steamId = GetCurrentSteamUserId();
                if (steamId == null)
                {
                    Logger.Log("Could not determine Steam user ID");
                    return null;
                }

                string screenshotPath = Path.Combine(steamPath, "userdata", steamId, "760", "remote", "2357570", "screenshots");
                return screenshotPath;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error getting Steam screenshot path: {ex.Message}");
                return null;
            }
        }

        private string? GetCurrentSteamUserId()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    if (key == null)
                    {
                        Logger.Log("Steam registry key not found");
                        return null;
                    }

                    var activeUser = key.GetValue("ActiveUser");
                    if (activeUser != null && activeUser.ToString() != "0")
                    {
                        return activeUser.ToString();
                    }
                }

                // Fallback to LastUserKey if ActiveProcess doesn't have it
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var lastUser = key?.GetValue("AutoLoginUser");
                    if (lastUser != null && !string.IsNullOrEmpty(lastUser.ToString()))
                    {
                        Logger.Log("Using Steam AutoLoginUser as fallback");
                        // Get the corresponding ID from the login users list
                        using (var loginUsersKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Users"))
                        {
                            if (loginUsersKey != null)
                            {
                                foreach (var userKeyName in loginUsersKey.GetSubKeyNames())
                                {
                                    using (var userKey = loginUsersKey.OpenSubKey(userKeyName))
                                    {
                                        var accountName = userKey?.GetValue("AccountName");
                                        if (accountName?.ToString() == lastUser.ToString())
                                        {
                                            return userKeyName;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Logger.Log("No active Steam user found");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error getting Steam user ID: {ex.Message}");
                return null;
            }
        }

        private void AddWatcher(string path)
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                    Filter = "*.jpg"
                };

                var pngWatcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                    Filter = "*.png"
                };

                watcher.Created += OnFileCreated;
                pngWatcher.Created += OnFileCreated;

                watchers[path] = watcher;
                watchers[path + "_png"] = pngWatcher;

                Logger.Log($"Started monitoring folder: {path}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error setting up watcher for path {path}: {ex.Message}");
            }
        }

        private void OnFileCreated(object? sender, FileSystemEventArgs e)
        {
            synchronizationContext.Post(async _ =>
            {
                await Task.Delay(200); // Wait for the file to be fully written

                // Check if Overwatch is running
                if (!App.DEV_MODE && !Process.GetProcessesByName("Overwatch").Any())
                {
                    Logger.Log("Screenshot detected but Overwatch 2 is not running - ignoring");
                    return;
                }

                try
                {
                    Logger.Log($"[DEV] Processing file: {e.FullPath}");
                    // Notify that analysis is starting
                    AnalysisStarted?.Invoke(sender ?? this, EventArgs.Empty);

                    using (var image = Image.FromFile(e.FullPath))
                    using (var ms = new MemoryStream())
                    {
                        Logger.Log($"[DEV] Image size: {image.Width}x{image.Height}");
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        var imageBytes = ms.ToArray();
                        Logger.Log($"[DEV] Base64 length: {imageBytes.Length} bytes");
                        var base64String = Convert.ToBase64String(imageBytes);

                        var response = await apiService.SendScreenshotToServer(base64String);
                        if (response != null)
                        {
                            ScreenshotProcessed?.Invoke(sender ?? this, response);
                        }
                        else
                        {
                            Logger.Log("Received null response from screenshot processing");
                            AnalysisError?.Invoke(sender ?? this, "No response from server");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error processing screenshot: {ex.Message}");
                    AnalysisError?.Invoke(sender ?? this, "Error processing screenshot");
                }
            }, null);
        }

        public void Dispose()
        {
            foreach (var watcher in watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            watchers.Clear();
        }
    }
}