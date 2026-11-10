using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Owmeta.Services;

/// <summary>Opt-in local playtest evidence (set OWMETA_CAPTURE_DIR). Never prunes captures or
/// touches game originals. Disabled by default: sources contain other players' names and chat.</summary>
public sealed class CaptureJournal
{
    static string? ConfiguredRoot => Environment.GetEnvironmentVariable("OWMETA_CAPTURE_DIR") is { Length: > 0 } dir ? dir : null;
    public static bool Enabled => ConfiguredRoot != null;
    public static string Root => ConfiguredRoot ?? Path.Combine(AppContext.BaseDirectory, "recognition-captures");
    static readonly object Gate = new();
    static long? recordedBytes;
    const long MaxBytes = 10L * 1024 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string? DirectoryPath { get; private set; }
    public string? Warning { get; private set; }

    public CaptureJournal(byte[] source)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Root);
                recordedBytes ??= Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                if (recordedBytes + source.Length + 2_000_000 > MaxBytes || new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Root))!).AvailableFreeSpace < 512L * 1024 * 1024)
                    throw new IOException("Capture recording quota/free-space limit reached; existing captures retained");
                DirectoryPath = Path.Combine(Root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Id);
                Directory.CreateDirectory(DirectoryPath);
                // Reserve bounded room for request, responses and displayed state.
                recordedBytes += source.Length + 2_000_000;
            }
            string filename = source.Length > 1 && source[0] == 255 && source[1] == 216 ? "source.jpg" : source.Length > 3 && source[0] == 137 && source[1] == 80 && source[2] == 78 && source[3] == 71 ? "source.png" : "source.image";
            WriteBytes(filename, source);
            Write("capture.json", new
            {
                schema = 1,
                captureId = Id,
                utc = DateTime.UtcNow,
                sourceSha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
                hudVersion = typeof(CaptureJournal).Assembly.ManifestModule.ModuleVersionId,
                preparationVersion = "role-grid-v1",
                privacy = "Local only. Source may contain player names/chat. No authorization headers or tokens are recorded."
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Fail(e); }
    }
    public void Write(string filename, object value)
    {
        try { WriteBytes(filename, JsonSerializer.SerializeToUtf8Bytes(value, Json)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or JsonException) { Fail(e); }
    }
    public void WriteBytes(string filename, byte[] bytes)
    {
        if (DirectoryPath is null) return;
        if (Path.GetFileName(filename) != filename) throw new ArgumentException("Journal filename must be local", nameof(filename));
        try { using var stream = new FileStream(Path.Combine(DirectoryPath, filename), FileMode.CreateNew, FileAccess.Write, FileShare.Read); stream.Write(bytes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Fail(e); }
    }
    void Fail(Exception error) { Warning = error.Message; Logger.Log("Recognition recording warning: " + error.Message); }
}
