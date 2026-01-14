using System.IO;
using System.Text.Json;

namespace Owmeta.Services
{
    public class AppSettings
    {
        public int MinScoreF2 { get; set; } = 0;
        public int MinScoreF3 { get; set; } = 2;
        public bool TabScreenshotEnabled { get; set; } = true;

        // Keybindings - stored as virtual key codes (0 = disabled, negative = mouse button)
        // Mouse buttons: -1 = XButton1 (Mouse4), -2 = XButton2 (Mouse5)
        public int SwapSuggestionsKey { get; set; } = 0x71;  // F2
        public int TeamCompositionKey { get; set; } = 0x72;  // F3
        public int ScreenshotKey { get; set; } = 0x09;       // TAB (should match OW scoreboard key)

        // Delay before capturing screenshot (ms) - allows scoreboard to render
        public int ScreenshotDelayMs { get; set; } = 150;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OWMetaHUD",
            "settings.json");

        private static AppSettings? _instance;
        public static AppSettings Instance => _instance ??= Load();

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading settings: {ex.Message}");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error saving settings: {ex.Message}");
            }
        }
    }
}
