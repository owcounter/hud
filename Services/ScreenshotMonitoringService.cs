using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Owcounter.Services
{
    public class ScreenshotMonitoringService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>();
        private readonly ApiService apiService;
        private readonly SynchronizationContext synchronizationContext;
        public event EventHandler<Model.ScreenshotProcessingResponse>? ScreenshotProcessed;

        public ScreenshotMonitoringService(ApiService apiService)
        {
            this.apiService = apiService;
            synchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }
        public void StartMonitoring()
        {
            // Monitor Battle.net path
            string bnetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Overwatch", "ScreenShots", "Overwatch");
            if (Directory.Exists(bnetPath))
            {
                AddWatcher(bnetPath);
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
            if (App.DEV_MODE && Directory.Exists(bnetPath))
            {
                var latestFile = Directory.GetFiles(bnetPath, "*.jpg")
                    .Concat(Directory.GetFiles(bnetPath, "*.png"))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (latestFile != null)
                {
                    var directory = Path.GetDirectoryName(latestFile);
                    if (directory != null)
                    {
                        Task.Delay(200).Wait(); // Keep the file write delay
                        OnFileCreated(this, new FileSystemEventArgs(WatcherChangeTypes.Created, directory, Path.GetFileName(latestFile)));
                    }
                }
            }
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
                    using (var image = Image.FromFile(e.FullPath))
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        var imageBytes = ms.ToArray();
                        var base64String = Convert.ToBase64String(imageBytes);

                        var response = await apiService.SendScreenshotToServer(base64String);
                        if (response != null)
                        {
                            ScreenshotProcessed?.Invoke(sender ?? this, response);
                        }
                        else
                        {
                            Logger.Log("Received null response from screenshot processing");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error processing screenshot: {ex.Message}");
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