using Owmeta.Model;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Owmeta.Services
{
    public class ScreenshotMonitoringService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();
        private readonly ApiService apiService;
        private readonly SynchronizationContext synchronizationContext;
        private readonly ScreenshotCaptureThrottle captureThrottle = new(TimeSpan.FromSeconds(2));
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
                if (App.AutoTestMode)
                {
                    RunAutoTest(bnetPath);
                }
                else
                {
                    ProcessLatestScreenshotForDev(bnetPath);
                }
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

        private void RunAutoTest(string bnetPath)
        {
            string screenshotsPath = App.AutoTestDir ?? bnetPath;

            if (!Directory.Exists(screenshotsPath))
            {
                Logger.Log($"[AUTO-TEST] Screenshot directory not found: {screenshotsPath}");
                return;
            }

            LoadAllScreenshots(screenshotsPath);
            Logger.Log($"[AUTO-TEST] Found {_allScreenshots.Count} screenshots in {screenshotsPath}");
            Logger.Log($"[AUTO-TEST] Processing newest first, {App.AutoTestIntervalSeconds}s interval");

            _ = Task.Run(async () =>
            {
                var stopwatch = new Stopwatch();
                for (int i = 0; i < _allScreenshots.Count; i++)
                {
                    var file = _allScreenshots[i];
                    var fileName = Path.GetFileName(file);

                    Logger.Log($"\n[AUTO-TEST] === [{i + 1}/{_allScreenshots.Count}] {fileName} ===");

                    try
                    {
                        var originalBytes = await ReadOriginalScreenshot(file);
                        var base64String = Convert.ToBase64String(originalBytes);
                        Logger.Log($"[AUTO-TEST] Sending {originalBytes.Length} original bytes...");

                        stopwatch.Restart();

                        synchronizationContext.Post(_ =>
                        {
                            AnalysisStarted?.Invoke(this, EventArgs.Empty);
                        }, null);

                        var response = await apiService.SendScreenshotToServer(base64String);
                        stopwatch.Stop();

                        if (response != null)
                        {
                            LogAutoTestResponse(response, stopwatch.ElapsedMilliseconds);
                            synchronizationContext.Post(_ =>
                            {
                                ScreenshotProcessed?.Invoke(this, response);
                            }, null);
                        }
                        else
                        {
                            Logger.Log($"[AUTO-TEST] NULL response ({stopwatch.ElapsedMilliseconds}ms)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[AUTO-TEST] ERROR: {ex.Message}");
                        synchronizationContext.Post(_ =>
                        {
                            AnalysisError?.Invoke(this, ex.Message);
                        }, null);
                    }

                    if (i < _allScreenshots.Count - 1)
                    {
                        Logger.Log($"[AUTO-TEST] Waiting {App.AutoTestIntervalSeconds}s...");
                        await Task.Delay(App.AutoTestIntervalSeconds * 1000);
                    }
                }
                Logger.Log($"\n[AUTO-TEST] === Done ({_allScreenshots.Count} screenshots) ===");
            });
        }

        private static void LogAutoTestResponse(ScreenshotProcessingResponse response, long elapsedMs)
        {
            var ms = response.MatchState;
            Logger.Log($"[AUTO-TEST] Response in {elapsedMs}ms:");
            if (ms.BlueTeam != null)
            {
                Logger.Log($"[AUTO-TEST]   BLUE: T={ms.BlueTeam.Tank} D1={ms.BlueTeam.Damage1} D2={ms.BlueTeam.Damage2} S1={ms.BlueTeam.Support1} S2={ms.BlueTeam.Support2}");
            }
            if (ms.RedTeam != null)
            {
                Logger.Log($"[AUTO-TEST]   RED:  T={ms.RedTeam.Tank} D1={ms.RedTeam.Damage1} D2={ms.RedTeam.Damage2} S1={ms.RedTeam.Support1} S2={ms.RedTeam.Support2}");
            }
            if (ms.Map != MapName.MapUnspecified)
            {
                Logger.Log($"[AUTO-TEST]   Map: {ms.Map}");
            }
            if (ms.PlayerHero != HeroName.NameUnspecified)
            {
                Logger.Log($"[AUTO-TEST]   Player: {ms.PlayerHero}");
            }
            if (response.PersistedBlueTeamSlots.Count > 0)
            {
                Logger.Log($"[AUTO-TEST]   Persisted Blue: {string.Join(", ", response.PersistedBlueTeamSlots)}");
            }
            if (response.PersistedRedTeamSlots.Count > 0)
            {
                Logger.Log($"[AUTO-TEST]   Persisted Red: {string.Join(", ", response.PersistedRedTeamSlots)}");
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        public void CaptureAndProcessScreenshot(CancellationToken cancellation = default, int heldKey = 0)
        {
            var captureQueuedAt = Stopwatch.GetTimestamp();
            synchronizationContext.Post(async _ =>
            {
                HudDiagnostics.Write($"capture-dispatched queueMs={Stopwatch.GetElapsedTime(captureQueuedAt).TotalMilliseconds:F2}");
                // Check if Overwatch is running (unless in dev mode)
                var overwatchProcess = Process.GetProcessesByName("Overwatch").FirstOrDefault();
                if (!App.DEV_MODE && overwatchProcess == null)
                {
                    Logger.Log("TAB pressed but Overwatch is not running - ignoring");
                    return;
                }

                if (!captureThrottle.TryBeginCapture(DateTimeOffset.UtcNow, out var throttleReason))
                {
                    Logger.Log($"Screenshot capture skipped: {throttleReason}");
                    return;
                }

                bool analyzing = false;
                try
                {
                    HudDiagnostics.Write($"capture-start delayMs={AppSettings.Instance.ScreenshotDelayMs}");
                    // Wait for the scoreboard to appear after TAB is pressed
                    await Task.Delay(Math.Max(50, AppSettings.Instance.ScreenshotDelayMs), cancellation);

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
                    var base64String = await Task.Run(() =>
                    {
                        void CheckHeld() {
                            ScreenshotCaptureGuard.Check(cancellation,
                                () => heldKey == 0 || (GetAsyncKeyState(heldKey) & 0x8000) != 0);
                        }
                        CheckHeld();
                        var copyStarted = Stopwatch.GetTimestamp();
                        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(bitmap))
                            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                        // Reject a release during the copy, but do not cancel an already
                        // captured scoreboard while encoding or awaiting the worker.
                        CheckHeld();
                        HudDiagnostics.Write($"capture-copy elapsedMs={Stopwatch.GetElapsedTime(copyStarted).TotalMilliseconds:F2} size={bounds.Width}x{bounds.Height}");
                        using var ms = new MemoryStream();
                        var encodeStarted = Stopwatch.GetTimestamp();
                        bitmap.Save(ms, ImageFormat.Png);
                        HudDiagnostics.Write($"capture-encode elapsedMs={Stopwatch.GetElapsedTime(encodeStarted).TotalMilliseconds:F2} bytes={ms.Length}");
                        return Convert.ToBase64String(ms.ToArray());
                    });
                    analyzing = true;
                    AnalysisStarted?.Invoke(this, EventArgs.Empty);
                    // Preparation, JSON/base64 and evidence writes also stay off the UI dispatcher.
                    var response = await Task.Run(() => apiService.SendScreenshotToServer(base64String));
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
                // Only the delay/copy phase is cancelled by a key release; HttpClient timeouts
                // during analysis also throw OperationCanceledException and must surface as errors.
                catch (OperationCanceledException) when (!analyzing)
                {
                    HudDiagnostics.Write("capture-canceled key released before pixels were captured");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error capturing/processing screenshot: {ex.Message}");
                    AnalysisError?.Invoke(this, "Error capturing screenshot");
                }
                finally
                {
                    captureThrottle.CompleteCapture();
                    HudDiagnostics.Write($"capture-complete elapsedMs={Stopwatch.GetElapsedTime(captureQueuedAt).TotalMilliseconds:F2}");
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

                if (!captureThrottle.TryBeginCapture(DateTimeOffset.UtcNow, out var throttleReason))
                {
                    Logger.Log($"Screenshot file skipped: {throttleReason}");
                    return;
                }

                try
                {
                    Logger.Log($"[DEV] Processing file: {e.FullPath}");
                    // Notify that analysis is starting
                    AnalysisStarted?.Invoke(sender ?? this, EventArgs.Empty);

                    {
                        var imageBytes = await ReadOriginalScreenshot(e.FullPath);
                        Logger.Log($"[DEV] Base64 length: {imageBytes.Length} bytes");
                        var response = await Task.Run(() => apiService.SendScreenshotToServer(Convert.ToBase64String(imageBytes)));
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
                finally
                {
                    captureThrottle.CompleteCapture();
                }
            }, null);
        }

        public static async Task<byte[]> ReadOriginalScreenshot(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > Owmeta.Vision.Preparation.FramePreparer.MaximumImageBytes)
                throw new InvalidDataException("Screenshot file size is unsupported");
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes);
            return bytes;
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
