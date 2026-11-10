using Newtonsoft.Json;
using Owmeta.Model;
using Owmeta.Services;

internal static class ServerHistoryTests
{
    public static void Run()
    {
        const string json = """
        {"matchState":{"blueTeam":{"tank":30},"redTeam":{"tank":10},"playerHero":9,"map":36},
         "recognizedMatchState":{"blueTeam":{"tank":30},"redTeam":{"tank":1},"playerHero":1,"map":0},
         "matchStateAnalysis":{"blueTeamAnalysis":{"30":{"hero":{"name":30}}},"redTeamAnalysis":{}},
         "playerAnalysis":{"hero":{"name":9},"swapSuggestions":[]},
         "persistedPlayerHero":true,"persistedRedTeamSlots":["tank"]}
        """;
        var result = ServerRecognitionContract.ReadResponse(json);
        if ((int)result.MatchState.PlayerHero != 9 || !result.PersistedPlayerHero || (int)result.RecognizedMatchState!.PlayerHero != 1)
            throw new Exception("Server history or raw observation lost");
        var player = ServerRecognitionContract.PlayerAnalyses(result);
        if (!player.ContainsKey(HeroName.Dva) || result.BlueTeamAnalysis.ContainsKey(HeroName.Dva))
            throw new Exception("Main-player analysis was lost or invented an extra teammate");
        var selected = SelectedHeroPolicy.ResolveForSwapPanel(result.MatchState.PlayerHero, null, player);
        if (selected.Hero != HeroName.Dva) throw new Exception("Retained main player did not reach the swap panel");
        var roundtrip = JsonConvert.SerializeObject(result, new ScreenshotProcessingResponseConverter());
        if (!ServerRecognitionContract.ReadResponse(roundtrip).PersistedPlayerHero) throw new Exception("Persistence flag failed roundtrip");
        result.MatchState.PlayerHero = HeroName.Hidden;
        if (SelectedHeroPolicy.ResolveForSwapPanel(result.MatchState.PlayerHero, null, player).Hero.HasValue)
            throw new Exception("HUD restored an explicitly hidden main player");
        foreach (var invalid in new[] { "{\"matchStateAnalysis\":{}}", "{\"matchState\":{\"playerHero\":9}}" })
        {
            try { ServerRecognitionContract.ReadResponse(invalid); throw new Exception("Unsupported backend accepted"); }
            catch (ApiException) { }
        }
        var failed = ServerRecognitionContract.ReadResponse(json.Replace("\"persistedPlayerHero\":true", "\"recognitionFailed\":true,\"persistedPlayerHero\":true"));
        if (!failed.RecognitionFailed || failed.MatchState.PlayerHero != HeroName.Dva) throw new Exception("Whole-frame historical result was lost");
        Console.WriteLine("PASS server-owned history, raw UNKNOWN, independent main-player suggestions, HIDDEN, rejected frames and old-API rejection");
    }
}
