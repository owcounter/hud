using System.IO;

namespace Owmeta
{
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OwmetaHUD.log");
        private const int MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB

        public static void Log(string message)
        {
            try
            {
                CheckLogFileSize();
                File.AppendAllText(LogFilePath, $"[{DateTime.Now}] {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                // If we can't log to file, we're pretty much screwed, but let's not crash the app
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }

        private static void CheckLogFileSize()
        {
            try
            {
                if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                {
                    File.Delete(LogFilePath);
                }
            }
            catch (IOException)
            {
                // File is probably locked. We'll just let it grow for now.
                // It'll get deleted next time it's not locked.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error managing log file: {ex.Message}");
            }
        }
    }
}