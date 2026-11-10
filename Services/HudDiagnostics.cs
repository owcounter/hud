using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Windows.Threading;

namespace Owmeta.Services;

/// <summary>Opt-in playtest timings; disk writes never run on the input/UI thread.</summary>
public static class HudDiagnostics
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("OWMETA_HUD_DIAGNOSTICS") == "1";
    private static readonly Channel<string> Queue = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    private static long dropped;
    private static int stopped;
    private static readonly Task Writer = Enabled ? Task.Run(WriteLoop) : Task.CompletedTask;

    public static void Write(string message)
    {
        if (!Enabled || Volatile.Read(ref stopped) != 0) return;
        var line = $"{DateTimeOffset.UtcNow:O} tick={Stopwatch.GetTimestamp()} thread={Environment.CurrentManagedThreadId} {message}";
        if (!Queue.Writer.TryWrite(line)) Interlocked.Increment(ref dropped);
    }

    public static IDisposable Measure(string operation, double minimumMs = 0) => new Measurement(operation, minimumMs);

    public static DispatcherTimer? StartHeartbeat(Dispatcher dispatcher)
    {
        if (!Enabled) return null;
        var last = Stopwatch.GetTimestamp();
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
            last = now;
            if (elapsed > 180) Write($"ui-heartbeat-gap elapsedMs={elapsed:F2} nominalMs=100");
        };
        timer.Start();
        return timer;
    }

    private sealed class Measurement(string operation, double minimumMs) : IDisposable
    {
        private readonly long start = Stopwatch.GetTimestamp();
        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (elapsed >= minimumMs) Write($"span operation={operation} elapsedMs={elapsed.ToString("F2", CultureInfo.InvariantCulture)}");
        }
    }

    private static async Task WriteLoop()
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("OWMETA_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(AppContext.BaseDirectory, "recognition-captures");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"hud-timing-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteLineAsync($"session pid={Environment.ProcessId} build={typeof(HudDiagnostics).Assembly.ManifestModule.ModuleVersionId:N} stopwatchFrequency={Stopwatch.Frequency}; timings are CPU/dispatcher observations, not display-present timestamps");
            await writer.FlushAsync();
            while (await Queue.Reader.WaitToReadAsync())
            {
                var lost = Interlocked.Exchange(ref dropped, 0);
                if (lost != 0) await writer.WriteLineAsync($"droppedDiagnosticEvents={lost}");
                for (var count = 0; count < 128 && Queue.Reader.TryRead(out var line); count++)
                    await writer.WriteLineAsync(line);
                await writer.FlushAsync();
                // Stop rather than deleting old diagnostics or growing without a limit.
                if (stream.Position >= 16 * 1024 * 1024)
                {
                    await writer.WriteLineAsync("diagnostics stopped: 16 MiB session limit reached");
                    break;
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"HUD diagnostics unavailable: {ex.GetType().Name}"); }
        finally { Volatile.Write(ref stopped, 1); Queue.Writer.TryComplete(); }
    }
}
