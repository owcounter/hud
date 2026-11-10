using System.IO;
using Owmeta.Model;
using Owmeta.Services;
using Owmeta.Vision.Protocol;

internal static class RecognitionTests
{
    /// <summary>Recognizes a real scoreboard screenshot with the bundled model pack.</summary>
    public static async Task RunLive(string screenshot)
    {
        var bytes=await ScreenshotMonitoringService.ReadOriginalScreenshot(screenshot);
        var capture=new CaptureJournal(bytes);
        using var recognizer=new LocalRecognizer();
        var timer=System.Diagnostics.Stopwatch.StartNew();
        await recognizer.Recognize(bytes,capture);
        long loadMs=timer.ElapsedMilliseconds;
        timer.Restart();
        var result=await recognizer.Recognize(bytes,new CaptureJournal(bytes));
        if(result.Raw.Slots.Length!=10 || result.State.BlueTeam==null || result.State.RedTeam==null) throw new Exception("Invalid local recognition result");
        static string Team(TeamComposition t) => $"{t.Tank} {t.Damage1} {t.Damage2} {t.Support1} {t.Support2}";
        Console.WriteLine($"PASS live local recognition: first call {loadMs} ms (includes loading), next {timer.ElapsedMilliseconds} ms; {result.Raw.Slots.Count(s=>s.Hero>2)}/10 recognized, player={result.State.PlayerHero}, map={result.State.Map}");
        Console.WriteLine($"  blue: {Team(result.State.BlueTeam)}");
        Console.WriteLine($"  red:  {Team(result.State.RedTeam)}");
    }

    public static void Run()
    {
        var deadSlots=Enumerable.Range(0,10).Select(_=>new SlotObservation(1,.4f,.01f,1,2,"unreadable-artwork")).ToArray();
        var panel=new SlotObservation(9,.95f,.5f,9,28,"accepted");
        var dead=new RecognitionResult {Valid=true,Slots=deadSlots,PlayerHero=9,SelectedRow=0,
            PlayerEvidence=new(9,0,"panel-portrait+name+selected-row",75,panel)};
        var resolved=LocalRecognizer.ToMatchState(dead);
        if((int)resolved.BlueTeam.Tank!=9 || (int)resolved.PlayerHero!=9 || dead.Slots[0].Hero!=1)
            throw new Exception("Independent current panel evidence was lost or raw portrait was overwritten");
        foreach(var badPanel in new[]{dead with {SelectedRow=1},dead with {PlayerHero=30},
            dead with {PlayerEvidence=dead.PlayerEvidence with {Portrait=panel with {Similarity=float.NaN}}}}) {
            try {LocalRecognizer.ToMatchState(badPanel);throw new Exception("Invalid panel evidence accepted");}catch(ApiException){}
        }
        var slots=Enumerable.Range(0,10).Select(_=>new SlotObservation(1,.4f,.01f,31,30,"ambiguous-identity")).ToArray();
        slots[0]=new(31,.99f,.4f,31,30,"accepted"); slots[5]=new(2,.99f,.4f,2,1,"explicitly-hidden");
        var raw=new RecognitionResult { Valid=true,Slots=slots,PlayerHero=31,Map=23 };
        var state=LocalRecognizer.ToMatchState(raw);
        if(state.BlueTeam.Tank!=HeroName.Sigma || state.BlueTeam.Damage1!=HeroName.Unknown || state.RedTeam.Tank!=HeroName.Hidden)
            throw new Exception("Raw UNKNOWN/HIDDEN meanings were not preserved");
        if(raw.Slots[1].Hero!=1) throw new Exception("Raw observation was mutated");
        foreach(var bad in new[]{raw with {Valid=false},raw with {Slots=[]},raw with {Slots=null!}}) {
            try {LocalRecognizer.ToMatchState(bad);throw new Exception("Invalid recognition result accepted");}catch(ApiException){}
        }
        slots[0]=slots[0] with {Similarity=0};
        try {LocalRecognizer.ToMatchState(raw);throw new Exception("Missing confidence accepted as a known hero");}catch(ApiException){}
        slots[0]=slots[0] with {Similarity=float.NaN};
        try {LocalRecognizer.ToMatchState(raw);throw new Exception("NaN accepted");}catch(ApiException){}
        if(ScreenshotMonitoringService.DescribeError(new RecognitionRejectedException("x",raw))!="Scoreboard not found · open it and try again"
            || ScreenshotMonitoringService.DescribeError(new RecognitionUnavailableException("x",new IOException()))!="Recognition unavailable · restart or reinstall the HUD"
            || ScreenshotMonitoringService.DescribeError(new RecognitionUnavailableException("x",new TypeInitializationException("Tesseract",new DllNotFoundException("onnxruntime"))))!="Recognition unavailable · install the Visual C++ x64 runtime"
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
        Console.WriteLine("PASS: recognition result to match state, UNKNOWN/HIDDEN, invalid results, error messages, source-byte preservation, non-overwriting journal, WPF empty states");
    }
}
