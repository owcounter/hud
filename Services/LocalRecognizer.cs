using System.Diagnostics;
using System.IO;
using Owmeta.Model;
using Owmeta.Vision;
using Owmeta.Vision.Preparation;
using Owmeta.Vision.Protocol;

namespace Owmeta.Services;

/// <summary>
/// Recognizes scoreboards on this PC with the bundled model pack: frame preparation, portrait encoder
/// and OCR run in-process, no worker and no screenshot upload. Only the recognized match state goes to
/// the API, which merges it with the previous state and computes the analysis.
/// </summary>
public sealed class LocalRecognizer : IDisposable
{
    /// <summary>The model pack ships next to the executable.</summary>
    public static string DefaultPackPath => Path.Combine(AppContext.BaseDirectory, "model-pack");

    readonly Lazy<Task<VisionEngine>> engine;

    public LocalRecognizer(string? packPath = null)
    {
        var pack = packPath ?? DefaultPackPath;
        // Loading verifies the pack checksums and warms the encoder and OCR (about a second):
        // start it early and off the UI thread, every capture awaits the same engine.
        engine = new(() => Task.Run(() => Load(pack)));
    }

    /// <summary>Starts loading the engine in the background.</summary>
    public void Warmup() => _ = engine.Value.ContinueWith(t =>
    {
        if (t.Exception != null) Logger.Log($"Recognition engine failed to load: {t.Exception.GetBaseException().Message}");
    }, TaskScheduler.Default);

    static VisionEngine Load(string pack)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var vision = new VisionEngine(pack);
            Logger.Log($"Recognition engine ready in {timer.ElapsedMilliseconds} ms (pack {vision.PackId[..12]})");
            return vision;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new RecognitionUnavailableException($"Recognition engine unavailable: {e.Message}", e);
        }
    }

    public async Task<(MatchState State, RecognitionResult Raw)> Recognize(byte[] image, CaptureJournal journal)
    {
        PreparedFrame frame;
        try { frame = await Task.Run(() => FramePreparer.Prepare(image, journal.Id)); }
        catch (Exception e) when (e is InvalidDataException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException)
        {
            var rejected = new RecognitionResult { CaptureId = journal.Id, Valid = false, Reason = "preparation-rejected: " + e.Message };
            journal.Write("preparation-rejection.json", rejected);
            throw new RecognitionRejectedException("Screenshot rejected: " + e.Message, rejected);
        }
        journal.WriteBytes("portraits.owp", frame.Portraits);
        journal.WriteBytes("map.png", frame.MapPng);
        journal.WriteBytes("player-name.png", frame.PlayerNamePng);
        journal.WriteBytes("player-portrait.owp", frame.PlayerPortrait);

        var vision = await engine.Value;
        var result = await Task.Run(() => vision.Process(frame));
        journal.Write("recognition.json", result);
        journal.Write("timing.json", new { frame.PreparationMs, result.IdentityMs, result.WorkerMs });
        if (!result.Valid) throw new RecognitionRejectedException("Screenshot rejected: " + result.Reason, result);
        var state = ToMatchState(result);
        Logger.Log($"Recognition {journal.Id}: {result.Slots.Count(s => s.Hero > 2)}/10 known, map={state.Map}, identity={result.IdentityMs:F1}ms total={result.WorkerMs:F1}ms preparation={frame.PreparationMs:F1}ms");
        return (state, result);
    }

    /// <summary>
    /// The recognized match state: UNKNOWN (1) and HIDDEN (2) keep their meaning so the API can retain
    /// previous heroes; a selected-panel identity fills an unreadable selected row only when its
    /// evidence is complete.
    /// </summary>
    public static MatchState ToMatchState(RecognitionResult result)
    {
        if (!result.Valid || result.Slots is not { Length: 10 } || result.Slots.Any(s => s is null || s.Hero < 1 || !Enum.IsDefined((HeroName)s.Hero)
            || !float.IsFinite(s.Similarity) || !float.IsFinite(s.Margin)
            || (s.Hero > 1 && (s.Similarity <= 0 || s.Margin <= 0 || s.BestCandidate != s.Hero)))) throw new ApiException("Invalid recognition identities");
        var heroes = result.Slots.Select(s => s.Hero).ToArray();
        if (result.PlayerEvidence is { Source: "panel-portrait+name+selected-row" } evidence)
        {
            if (evidence.Row is < 0 or > 4 || evidence.Row != result.SelectedRow || evidence.Hero != result.PlayerHero
                || !Enum.IsDefined((HeroName)evidence.Hero) || evidence.Hero <= 2 || heroes[evidence.Row] != 1
                || evidence.Portrait is not { } p || p.Hero != evidence.Hero || p.BestCandidate != p.Hero || p.Reason != "accepted"
                || !float.IsFinite(p.Similarity) || !float.IsFinite(p.Margin) || p.Similarity <= 0 || p.Margin <= 0)
                throw new ApiException("Invalid selected-panel evidence");
            heroes[evidence.Row] = evidence.Hero;
        }
        TeamComposition Team(int start) => new()
        {
            Tank = (HeroName)heroes[start],
            Damage1 = (HeroName)heroes[start + 1],
            Damage2 = (HeroName)heroes[start + 2],
            Support1 = (HeroName)heroes[start + 3],
            Support2 = (HeroName)heroes[start + 4]
        };
        return new()
        {
            BlueTeam = Team(0),
            RedTeam = Team(5),
            PlayerHero = Enum.IsDefined((HeroName)result.PlayerHero) ? (HeroName)result.PlayerHero : HeroName.Unknown,
            Map = Enum.IsDefined((MapName)result.Map) ? (MapName)result.Map : MapName.MapUnspecified
        };
    }

    public void Dispose()
    {
        if (engine.IsValueCreated && engine.Value.IsCompletedSuccessfully) engine.Value.Result.Dispose();
    }
}

/// <summary>
/// The screenshot is not a readable scoreboard. Only this case asks the API to reuse the previous
/// state; an unavailable engine or invalid result stays an error.
/// </summary>
public sealed class RecognitionRejectedException(string message, RecognitionResult raw) : ApiException(message)
{
    public RecognitionResult Raw { get; } = raw;
}

/// <summary>The bundled model pack or the recognition runtime could not be loaded.</summary>
public sealed class RecognitionUnavailableException(string message, Exception inner) : Exception(message, inner);
