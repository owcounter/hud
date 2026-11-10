using System.IO;
using Owmeta.Model;
using Owmeta.Services;

internal static class RecognitionTests
{
    public static void Run()
    {
        // Server error bodies map to what the player can do about them.
        if(ServerErrors.From("{\"message\":\"ERR_SEND_TO_WORKER: ERR_INVALID_SCREENSHOT\"}")!=ServerError.NotAScoreboard
            || ServerErrors.From("{\"message\":\"ERR_NO_AVAILABLE_WORKERS\"}")!=ServerError.NoWorker
            || ServerErrors.From("{\"message\":\"ERR_WORKER_TIMEOUT\"}")!=ServerError.WorkerFailed
            || ServerErrors.From("{\"message\":\"ERR_DB_INTERNAL\"}")!=ServerError.Unknown)
            throw new Exception("Server error body mapped to the wrong cause");
        if(ScreenshotMonitoringService.DescribeError(new ApiException("x",ServerError.NotAScoreboard))!="Scoreboard not found · open it and try again"
            || ScreenshotMonitoringService.DescribeError(new ApiException("x",ServerError.NoWorker))!="Analyzer offline · try again later"
            || ScreenshotMonitoringService.DescribeError(new UnauthorizedException("x"))!="Session expired · log in again"
            || ScreenshotMonitoringService.DescribeError(new System.Net.Http.HttpRequestException("x"))!="Server unreachable · try again")
            throw new Exception("Analysis error shown with the wrong message");
        var captureDir=Environment.GetEnvironmentVariable("OWMETA_CAPTURE_DIR");
        Environment.SetEnvironmentVariable("OWMETA_CAPTURE_DIR",null);
        var disabled=new CaptureJournal([255,216,0,1]); disabled.Write("test.json",new { rawUnknown=1 });
        if(disabled.DirectoryPath!=null || disabled.Warning!=null) throw new Exception("Capture journal recorded screenshots without opt-in");
        Environment.SetEnvironmentVariable("OWMETA_CAPTURE_DIR",captureDir ?? Path.Combine(AppContext.BaseDirectory,"recognition-captures"));
        var journal=new CaptureJournal([255,216,0,1]); journal.Write("test.json",new { rawUnknown=1,rawHidden=2 });
        if(journal.Warning!=null || journal.DirectoryPath==null || !File.Exists(Path.Combine(journal.DirectoryPath,"source.jpg")))
            throw new Exception("Capture journal did not preserve source bytes");
        if(!File.ReadAllBytes(Path.Combine(journal.DirectoryPath,"source.jpg")).SequenceEqual(new byte[]{255,216,0,1})) throw new Exception("Capture changed source pixels");
        if(!ScreenshotMonitoringService.ReadOriginalScreenshot(Path.Combine(journal.DirectoryPath,"source.jpg")).GetAwaiter().GetResult().SequenceEqual(new byte[]{255,216,0,1})) throw new Exception("File source was recompressed");
        journal.Write("test.json",new { overwrite=true });
        if(journal.Warning==null || File.ReadAllText(Path.Combine(journal.DirectoryPath,"test.json")).Contains("overwrite")) throw new Exception("Capture overwrite was not rejected");
        Exception? uiError=null;
        var uiThread=new Thread(()=> {
            try {
                var dashboard=new Owmeta.Display.CompositionDashboard(); dashboard.Update(new(),new());
                var header=(Owmeta.Display.CompositionHeader)dashboard.FindName("Header");
                if(header.AllyScoreText!="--" || header.EnemyScoreText!="--") throw new Exception("Empty team was presented as a zero score");
                if(header.Tendency.IsClear || header.Tendency.Recommended!=null) throw new Exception("Empty teams produced a composition conclusion");
                if(dashboard.Threats.Count!=0 || dashboard.Team.Count!=0) throw new Exception("Empty teams produced rows");
                var swaps=new Owmeta.Display.SwapSuggestionsPanel(); swaps.Clear("notice");
                if(swaps.CurrentHero!=null || swaps.Rows.Count!=0) throw new Exception("Cleared swap panel kept a hero");
                var ally=new Owmeta.Model.HeroAnalysis{ Hero=new Owmeta.Model.Hero{ Name=Owmeta.Model.HeroName.Ana }, HeroScore=2 };
                swaps.UpdateSuggestions(Owmeta.Model.HeroName.Unknown,new Dictionary<Owmeta.Model.HeroName,Owmeta.Model.HeroAnalysis>{ [Owmeta.Model.HeroName.Ana]=ally },null,"notice");
                var swapHeader=(Owmeta.Display.CompositionHeader)swaps.FindName("Header");
                if(swaps.CurrentHero!=null || swapHeader.AllyScoreText!="+2") throw new Exception("Unknown player hero dropped the team composition");
            } catch(Exception e) { uiError=e; }
        });
        uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start(); uiThread.Join();
        if(uiError!=null) throw new Exception("WPF empty-state regression",uiError);
        Console.WriteLine("PASS: server error messages, source-byte preservation, non-overwriting journal, WPF empty states");
    }
}
