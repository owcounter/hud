using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Owmeta.Model;

namespace Owmeta.Services;

public static class ServerRecognitionContract
{
    public static ScreenshotProcessingResponse ReadResponse(string json)
    {
        var envelope = JObject.Parse(json);
        // An older API returns analysis only. Never overwrite server state with
        // the raw observation or silently emulate its retention policy locally.
        if (envelope["matchState"] is not JObject)
            throw new ApiException("API does not support recognition history; update the backend before using this HUD build");
        var result = JsonConvert.DeserializeObject<ScreenshotProcessingResponse>(json, new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new StringEnumConverter(), new ScreenshotProcessingResponseConverter() }
        });
        if (result?.MatchState.BlueTeam == null || result.MatchState.RedTeam == null)
            throw new ApiException("Invalid server-merged match state");
        return result;
    }

    public static Dictionary<HeroName, HeroAnalysis> PlayerAnalyses(ScreenshotProcessingResponse response)
    {
        var result = new Dictionary<HeroName, HeroAnalysis>(response.BlueTeamAnalysis);
        if (response.PlayerAnalysis?.Hero?.Name == response.MatchState.PlayerHero && (int)response.MatchState.PlayerHero > 2)
            result[response.MatchState.PlayerHero] = response.PlayerAnalysis;
        return result;
    }
}
