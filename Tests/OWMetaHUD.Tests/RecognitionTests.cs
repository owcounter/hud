using System.IO;
using Owmeta.Model;
using Owmeta.Services;
using Owmeta.Vision.Protocol;

internal static class RecognitionTests
{
    public static async Task RunLive(string screenshot)
    {
        var bytes=await ScreenshotMonitoringService.ReadOriginalScreenshot(screenshot);
        var capture=new CaptureJournal(bytes);
        using var client=new RecognitionClient();
        var result=await client.Recognize(bytes,capture);
        capture.Write("client-state.json",new { result.State.BlueTeam,result.State.RedTeam,result.State.PlayerHero,result.State.Map });
        if(capture.Warning!=null) throw new Exception(capture.Warning);
        if(result.Raw.Slots.Length!=10 || result.State.BlueTeam==null || result.State.RedTeam==null) throw new Exception("Invalid live HUD recognition result");
        Console.WriteLine($"PASS live HUD preparation/client/worker/journal: {result.Raw.Slots.Count(s=>s.Hero>2)}/10 recognized, player={result.State.PlayerHero}, map={result.State.Map}; evidence={capture.DirectoryPath}");
    }

    public static void Run()
    {
        var deadSlots=Enumerable.Range(0,10).Select(_=>new SlotObservation(1,.4f,.01f,1,2,"unreadable-artwork")).ToArray();
        var panel=new SlotObservation(9,.95f,.5f,9,28,"accepted");
        var dead=new RecognitionResult {Valid=true,Slots=deadSlots,PlayerHero=9,SelectedRow=0,
            PlayerEvidence=new(9,0,"panel-portrait+name+selected-row",75,panel)};
        var resolved=RecognitionClient.ToMatchState(dead);
        if((int)resolved.BlueTeam.Tank!=9 || (int)resolved.PlayerHero!=9 || dead.Slots[0].Hero!=1)
            throw new Exception("Independent current panel evidence was lost or raw portrait was overwritten");
        foreach(var badPanel in new[]{dead with {SelectedRow=1},dead with {PlayerHero=30},
            dead with {PlayerEvidence=dead.PlayerEvidence with {Portrait=panel with {Similarity=float.NaN}}}}) {
            try {RecognitionClient.ToMatchState(badPanel);throw new Exception("Invalid panel evidence accepted");}catch(ApiException){}
        }
        var slots=Enumerable.Range(0,10).Select(_=>new SlotObservation(1,.4f,.01f,31,30,"ambiguous-identity")).ToArray();
        slots[0]=new(31,.99f,.4f,31,30,"accepted"); slots[5]=new(2,.99f,.4f,2,1,"explicitly-hidden");
        var raw=new RecognitionResult { Valid=true,Slots=slots,PlayerHero=31,Map=23 };
        var state=RecognitionClient.ToMatchState(raw);
        if(state.BlueTeam.Tank!=HeroName.Sigma || state.BlueTeam.Damage1!=HeroName.Unknown || state.RedTeam.Tank!=HeroName.Hidden)
            throw new Exception("Raw UNKNOWN/HIDDEN meanings were not preserved");
        if(raw.Slots[1].Hero!=1) throw new Exception("Raw observation was mutated");
        var envelope=raw with {CaptureId=Guid.NewGuid().ToString("N"),PackId=new string('a',64),WorkerBuildId=Guid.NewGuid().ToString("N")};
        RecognitionClient.ValidateEnvelope(envelope,envelope.CaptureId);
        foreach(var invalidEnvelope in new[]{envelope with {PackId=null!},envelope with {PackId=new string('z',64)},envelope with {WorkerBuildId=""}}) {
            try {RecognitionClient.ValidateEnvelope(invalidEnvelope,envelope.CaptureId);throw new Exception("Invalid provenance accepted");}catch(ApiException){}
        }
        foreach(var bad in new[]{raw with {Valid=false},raw with {Slots=[]},raw with {Slots=null!}}) {
            try {RecognitionClient.ToMatchState(bad);throw new Exception("Invalid worker result accepted");}catch(ApiException){}
        }
        slots[0]=slots[0] with {Similarity=0};
        try {RecognitionClient.ToMatchState(raw);throw new Exception("Missing confidence accepted as a known hero");}catch(ApiException){}
        slots[0]=slots[0] with {Similarity=float.NaN};
        try {RecognitionClient.ToMatchState(raw);throw new Exception("NaN accepted");}catch(ApiException){}
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
                var panel=new Owmeta.Display.TeamCompositionPanel();
                panel.UpdateCompositions(new());
                var score=(System.Windows.Controls.TextBlock)panel.FindName("TeamScoreText");
                if(score.Text!="--" || panel.Compositions.Count!=0) throw new Exception("Empty team was presented as a zero score");
                var dashboard=new Owmeta.Display.CompositionDashboard(); dashboard.Update(new(),new());
                var matchup=(System.Windows.Controls.TextBlock)dashboard.FindName("MatchupText");
                if(matchup.Text!="INSUFFICIENT DATA") throw new Exception("Empty teams produced a matchup conclusion");
            } catch(Exception e) { uiError=e; }
        });
        uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start(); uiThread.Join();
        if(uiError!=null) throw new Exception("WPF empty-state regression",uiError);
        Console.WriteLine("PASS: recognition contract, UNKNOWN/HIDDEN, invalid responses, source-byte preservation, non-overwriting journal");
    }
}
