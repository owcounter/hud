using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Owmeta.Model;
using Owmeta.Vision.Preparation;
using Owmeta.Vision.Protocol;

namespace Owmeta.Services;

public sealed class RecognitionClient : IDisposable
{
    readonly HttpClient client;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public RecognitionClient(HttpMessageHandler? handler = null)
    {
        client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = LocalWorkerAddress.Current;
        client.Timeout = TimeSpan.FromSeconds(2);
    }
    public async Task<(MatchState State, RecognitionResult Raw)> Recognize(byte[] image, CaptureJournal journal)
    {
        PreparedFrame frame;
        try { frame = await Task.Run(() => FramePreparer.Prepare(image, journal.Id)); }
        catch (Exception e) when (e is InvalidDataException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException)
        {
            var rejected = new RecognitionResult { CaptureId=journal.Id, Valid=false, Reason="preparation-rejected: " + e.Message };
            journal.Write("preparation-rejection.json", rejected);
            throw new RecognitionRejectedException("Screenshot rejected: " + e.Message, rejected);
        }
        var request = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
        journal.WriteBytes("request.json", request);
        journal.WriteBytes("portraits.owp", frame.Portraits);
        journal.WriteBytes("map.png", frame.MapPng);
        journal.WriteBytes("player-name.png", frame.PlayerNamePng);
        journal.WriteBytes("player-portrait.owp", frame.PlayerPortrait);
        using var content = new ByteArrayContent(request); content.Headers.ContentType = new("application/json");
        var timer = Stopwatch.StartNew();
        using var response = await client.PostAsync("/recognize", content);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        journal.WriteBytes("worker-response.json", bytes);
        if (!response.IsSuccessStatusCode) throw new ApiException($"Replacement worker rejected request ({(int)response.StatusCode})");
        var result = JsonSerializer.Deserialize<RecognitionResult>(bytes, Json) ?? throw new ApiException("Empty recognition response");
        ValidateEnvelope(result, journal.Id);
        journal.Write("timing.json", new { frame.PreparationMs, workerRoundtripMs = timer.Elapsed.TotalMilliseconds, result.IdentityMs, result.WorkerMs });
        if (!result.Valid) throw new RecognitionRejectedException("Screenshot rejected: " + result.Reason, result);
        var state = ToMatchState(result);
        Logger.Log($"Recognition {journal.Id}: {result.Slots.Count(s => s.Hero > 2)}/10 known, identity={result.IdentityMs:F2}ms worker={result.WorkerMs:F1}ms preparation={frame.PreparationMs:F1}ms; pack={result.PackId[..12]}");
        return (state, result);
    }
    public static MatchState ToMatchState(RecognitionResult result)
    {
        if (!result.Valid || result.Slots is not { Length: 10 } || result.Slots.Any(s => s is null || s.Hero < 1 || !Enum.IsDefined((HeroName)s.Hero)
            || !float.IsFinite(s.Similarity) || !float.IsFinite(s.Margin)
            || (s.Hero > 1 && (s.Similarity <= 0 || s.Margin <= 0 || s.BestCandidate != s.Hero)))) throw new ApiException("Invalid recognition identities");
        var heroes=result.Slots.Select(s=>s.Hero).ToArray();
        if(result.PlayerEvidence is {Source:"panel-portrait+name+selected-row"} evidence)
        {
            if(evidence.Row is <0 or >4 || evidence.Row!=result.SelectedRow || evidence.Hero!=result.PlayerHero
                || !Enum.IsDefined((HeroName)evidence.Hero) || evidence.Hero<=2 || heroes[evidence.Row]!=1
                || evidence.Portrait is not {} p || p.Hero!=evidence.Hero || p.BestCandidate!=p.Hero || p.Reason!="accepted"
                || !float.IsFinite(p.Similarity) || !float.IsFinite(p.Margin) || p.Similarity<=0 || p.Margin<=0)
                throw new ApiException("Invalid selected-panel evidence");
            heroes[evidence.Row]=evidence.Hero;
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
    public static void ValidateEnvelope(RecognitionResult result, string captureId)
    {
        if (result.Schema != 1 || result.CaptureId != captureId || result.PackId?.Length != 64 || !result.PackId.All(Uri.IsHexDigit)
            || !Guid.TryParseExact(result.WorkerBuildId, "N", out _)) throw new ApiException("Incompatible recognition response");
    }
    public void Dispose() => client.Dispose();
}
