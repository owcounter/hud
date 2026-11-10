using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Owmeta.Vision.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;

namespace Owmeta.Vision;

public sealed class VisionEngine : IDisposable
{
    readonly Recognizer recognizer;
    readonly TesseractEngine ocr;
    readonly JsonDocument catalog;
    readonly byte[][] roleTemplates;
    readonly int[] roleLabels;
    readonly string packId;
    readonly object gate = new();
    bool disposed;
    public string PackId => packId;
    public string BuildId => typeof(VisionEngine).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    public VisionEngine(string pack)
    {
        var metadataBytes = File.ReadAllBytes(Path.Combine(pack,"playtest-pack.json"));
        using var metadata = JsonDocument.Parse(metadataBytes);
        if(metadata.RootElement.GetProperty("schema").GetInt32()!=1) throw new InvalidDataException("Unsupported playtest model pack");
        foreach(var required in new[]{"manifest.json","gallery.bin","encoder.onnx","roles.bin","catalog.json","tessdata/eng.traineddata"})
            if(!metadata.RootElement.GetProperty("checksums").TryGetProperty(required,out _)) throw new InvalidDataException("Incomplete model checksums");
        foreach (var entry in metadata.RootElement.GetProperty("checksums").EnumerateObject()) {
            if (entry.Name.Contains("..") || Path.IsPathRooted(entry.Name)) throw new InvalidDataException("Invalid model filename");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(pack,entry.Name))));
            if (!hash.Equals(entry.Value.GetString(),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Model pack checksum mismatch");
        }
        packId = Convert.ToHexString(SHA256.HashData(metadataBytes)).ToLowerInvariant();
        try {
        catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack,"catalog.json")));
        var roles = File.ReadAllBytes(Path.Combine(pack,"roles.bin"));
        if (roles.Length==0 || roles.Length%193!=0 || roles.Length>19300) throw new InvalidDataException("Invalid role references");
        roleTemplates = Enumerable.Range(0,roles.Length/193).Select(i => roles.AsSpan(i*193+1,192).ToArray()).ToArray();
        roleLabels = Enumerable.Range(0,roleTemplates.Length).Select(i=>(int)roles[i*193]).ToArray();
        if(roleTemplates.Any(t=>t.Any(v=>v>1)) || roleLabels.Any(r=>r is <1 or >3)) throw new InvalidDataException("Invalid role mask");
        recognizer = new Recognizer(pack);
        var knownIds = catalog.RootElement.GetProperty("heroes").EnumerateObject().Select(p => int.Parse(p.Name)).ToHashSet();
        if (knownIds.Any(id => id <= 2 || !recognizer.HeroIds.Contains(id) || HeroRole(id) is < 1 or > 3)
            || recognizer.HeroIds.Any(id => id > 2 && !knownIds.Contains(id)))
            throw new InvalidDataException("Hero catalog, roles and gallery coverage disagree");
            ocr = new TesseractEngine(Path.Combine(pack,"tessdata"),"eng",EngineMode.Default);
            using var warm = new Image<Rgb24>(64,32); using var stream = new MemoryStream(); warm.SaveAsPng(stream);
            ReadText(stream.ToArray());
        } catch { ocr?.Dispose(); recognizer?.Dispose(); catalog?.Dispose(); throw; }
    }

    public RecognitionResult Process(PreparedFrame frame)
    {
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed,this);
            var timer = Stopwatch.StartNew(); Validate(frame);
            var roles = Enumerable.Range(0,10).Select(i => ReadRole(frame.RoleMasks.AsSpan(i*192,192))).ToArray();
            int[] expected = [1,2,2,3,3,1,2,2,3,3];
            if (roles.Where((role,i) => role.Role != expected[i]).Any())
                return new() { CaptureId=frame.CaptureId, PackId=packId, WorkerBuildId=BuildId, Reason="unsupported-or-unreadable-role-structure", Roles=roles, WorkerMs=timer.Elapsed.TotalMilliseconds };
            var identityTimer = Stopwatch.StartNew();
            var slots = recognizer.Process(frame.Portraits).Select(o => new SlotObservation(o.Hero,o.Similarity,o.Margin,o.BestCandidate,o.RunnerUp,o.Reason)).ToArray();
            double identityMs = identityTimer.Elapsed.TotalMilliseconds;
            for (int i=0;i<10;i++) if (slots[i].Hero>2 && HeroRole(slots[i].Hero)!=expected[i])
                return new() { CaptureId=frame.CaptureId, PackId=packId, WorkerBuildId=BuildId, Reason="hero-role-conflict", Slots=slots, Roles=roles, IdentityMs=identityMs, WorkerMs=timer.Elapsed.TotalMilliseconds };
            string mapText="", playerText="", ocrStatus="ok"; int map=0, player=1;
            TextWord[] mapWords=[], playerWords=[];
            var selectionScores=ReadSelectionEdges(frame.SelectionEdges);
            var selectionOrder=Enumerable.Range(0,5).OrderByDescending(i=>selectionScores[i]).ToArray();
            int selectedRow=selectionScores[selectionOrder[0]]>=35 && selectionScores[selectionOrder[0]]-selectionScores[selectionOrder[1]]>=20 ? selectionOrder[0] : -1;
            if(selectedRow>=0 && slots[selectedRow].Hero>2) player=slots[selectedRow].Hero;
            PlayerObservation? playerEvidence = player>2 ? new(player,selectedRow,"selected-row-portrait",0) : null;
            SlotObservation? panelPortrait=null;
            try {
                var mapRead=ReadText(frame.MapPng); mapText=mapRead.Text;
                mapWords=mapRead.Words;
                // Map titles are read correctly but with low word confidence (stylized font over the
                // scoreboard banner), so the confident-word filter used for hero names dropped every
                // map. A map needs a unique, complete, word-bounded catalog title in the full line.
                map=FindExactCatalog(mapRead.Text,"maps");
            } catch (Exception e) when (e is not OutOfMemoryException) { ocrStatus="map-unreadable:"+e.GetType().Name; }
            try {
                var playerRead=ReadText(frame.PlayerNamePng, true); playerText=playerRead.Text;
                playerWords=playerRead.Words;
                int candidate=FindExactCatalog(playerRead.ConfidentText,"heroes");
                if(selectedRow>=0 && slots[selectedRow].Hero==1 && frame.PlayerPortrait.Length>0) {
                    var panelTimer=Stopwatch.StartNew();
                    var p=recognizer.Process(frame.PlayerPortrait)[0];
                    panelPortrait=new(p.Hero,p.Similarity,p.Margin,p.BestCandidate,p.RunnerUp,p.Reason);
                    identityMs+=panelTimer.Elapsed.TotalMilliseconds;
                }
                playerEvidence=ResolvePlayer(slots,selectedRow,candidate,HeroRole(candidate),
                    playerWords.Where(w=>!string.IsNullOrWhiteSpace(w.Text)).Select(w=>w.OcrConfidence).DefaultIfEmpty(0).Min(),
                    panelPortrait,FindExactPanelName(playerRead.Text),panelPortrait is null ? 0 : HeroRole(panelPortrait.Hero));
                player=playerEvidence?.Hero ?? 1;
            } catch (Exception e) when (e is not OutOfMemoryException) { ocrStatus+=";player-unreadable:"+e.GetType().Name; }
            return new() { CaptureId=frame.CaptureId, PackId=packId, WorkerBuildId=BuildId, Valid=true, Reason="supported-role-queue", Slots=slots, Roles=roles,
                Map=map, PlayerHero=player, PlayerEvidence=playerEvidence, PlayerPortraitObservation=panelPortrait,
                MapText=mapText, PlayerText=playerText, MapWords=mapWords,PlayerWords=playerWords,
                SelectedRow=selectedRow,SelectionEdgeScores=selectionScores,OcrStatus=ocrStatus, IdentityMs=identityMs, WorkerMs=timer.Elapsed.TotalMilliseconds };
        }
    }

    public static PlayerObservation? ResolvePlayer(SlotObservation[] slots, int selectedRow, int panelHero, int panelRole, float confidence,
        SlotObservation? panelPortrait=null, int exactTextHero=0, int portraitRole=0)
    {
        bool panelAccepted=panelHero>2 && float.IsFinite(confidence) && confidence>=80;
        if(selectedRow is >=0 and <5) {
            int portraitHero=slots[selectedRow].Hero;
            int rowRole=selectedRow==0 ? 1 : selectedRow<3 ? 2 : 3;
            // Contradictory or explicitly hidden evidence must not be overwritten.
            if(panelAccepted && (panelRole!=rowRole || (portraitHero>2 && portraitHero!=panelHero))) return null;
            if(portraitHero>2) return new(portraitHero,selectedRow,"selected-row-portrait",0);
            // An unreadable row needs a confidently accepted independent portrait,
            // exact name corroboration and the explicit outline. Weak OCR alone is
            // never promoted into a known identity. Preserve the raw row as UNKNOWN.
            if(portraitHero==1 && panelPortrait is {Hero:>2, Reason:"accepted"} p
                && p.Hero==exactTextHero && portraitRole==rowRole && !slots.Take(5).Any(s=>s.Hero==p.Hero))
                return new(p.Hero,selectedRow,"panel-portrait+name+selected-row",confidence,p);
            return null;
        }
        // Without an outline, require a unique matching portrait as corroboration.
        var matches=Enumerable.Range(0,5).Where(i=>slots[i].Hero==panelHero).ToArray();
        return panelAccepted && matches.Length==1 ? new(panelHero,matches[0],"panel-text+portrait",confidence) : null;
    }

    public static void Validate(PreparedFrame f)
    {
        if (f.Schema!=1 || f.PreparationVersion!="role-grid-v1" || !Guid.TryParseExact(f.CaptureId,"N",out _)
            || f.Width<320 || f.Height<240 || f.Width>8192 || f.Height>8192 || (long)f.Width*f.Height>33_554_432
            || f.PortraitBoxes is not {Length:10} || f.RoleBoxes is not {Length:10}
            || f.RoleMasks is not {Length:1920} || f.RoleMasks.Any(v=>v>1)
            || f.Portraits is null || Recognizer.Validate(f.Portraits)!=10
            || f.MapPng is null || f.MapPng.Length>524288 || f.PlayerNamePng is null || f.PlayerNamePng.Length>524288
            || f.PlayerPortrait is null || (f.PlayerPortrait.Length>0 && Recognizer.Validate(f.PlayerPortrait)!=1)
            || f.SelectionEdges is not {Length:33600}
            || !double.IsFinite(f.PreparationMs) || f.PreparationMs<0)
            throw new InvalidDataException("Invalid prepared screenshot contract");
        if((f.PlayerPortrait.Length==0)!=(f.PlayerPortraitBox is null)
            || (f.PlayerPortraitBox is {} panel && (panel.X<0 || panel.Y<0 || panel.Width<=0 || panel.Height<=0
                || (long)panel.X+panel.Width>f.Width || (long)panel.Y+panel.Height>f.Height)))
            throw new InvalidDataException("Invalid player portrait geometry");
        foreach (var b in f.PortraitBoxes.Concat(f.RoleBoxes))
            if (b is null || b.X<0 || b.Y<0 || b.Width<=0 || b.Height<=0 || (long)b.X+b.Width>f.Width || (long)b.Y+b.Height>f.Height)
                throw new InvalidDataException("Invalid crop geometry");
        for (int i=0;i<10;i++) {
            var b=f.PortraitBoxes[i]; var r=f.RoleBoxes[i];
            if (Math.Abs(b.Width-b.Height)>2 || r.X+r.Width>b.X+2 || Math.Abs((r.Y+r.Height/2.0)-(b.Y+b.Height/2.0))>b.Height*.15)
                throw new InvalidDataException("Inconsistent role/portrait geometry");
            if (i%5>0) {
                var previous=f.PortraitBoxes[i-1];
                if (Math.Abs(b.X-previous.X)>3 || Math.Abs(b.Y-previous.Y-b.Height)>4 || Math.Abs(b.Height-previous.Height)>2)
                    throw new InvalidDataException("Inconsistent row spacing");
            }
        }
        if (f.PortraitBoxes[5].Y < f.PortraitBoxes[4].Y+f.PortraitBoxes[4].Height*1.5 || Math.Abs(f.PortraitBoxes[5].X-f.PortraitBoxes[0].X)>3)
            throw new InvalidDataException("Invalid team separation");
    }

    RoleObservation ReadRole(ReadOnlySpan<byte> glyph)
    {
        var scores=new float[3];
        for(int r=0;r<roleTemplates.Length;r++) { int intersection=0, union=0;
            for(int i=0;i<192;i++) { if(glyph[i]!=0 && roleTemplates[r][i]!=0) intersection++; if(glyph[i]!=0 || roleTemplates[r][i]!=0) union++; }
            scores[roleLabels[r]-1]=Math.Max(scores[roleLabels[r]-1],union==0 ? 0 : intersection/(float)union);
        }
        var order=Enumerable.Range(0,3).OrderByDescending(i=>scores[i]).ToArray();
        float score=scores[order[0]], margin=score-scores[order[1]];
        return new(score>=.80f && margin>=.08f ? order[0]+1 : 0,score,margin);
    }
    public static float[] ReadSelectionEdges(byte[] pixels)
    {
        if(pixels.Length!=33600) throw new InvalidDataException("Invalid selection strips");
        var result=new float[5];
        for(int row=0;row<5;row++) {
            float bestEdge=255;
            for(int edge=0;edge<2;edge++) {
                var lines=new float[7];
                for(int y=0;y<7;y++) {
                    // A broad horizontal edge, rather than a bright player name or statistic.
                    var values=new float[160];
                    for(int x=0;x<160;x++) { int p=(((row*2+edge)*7+y)*160+x)*3; values[x]=(pixels[p]+pixels[p+1]+pixels[p+2])/3f; }
                    Array.Sort(values); lines[y]=values[40];
                }
                bestEdge=Math.Min(bestEdge,lines.Skip(1).Take(5).Max()-Math.Min(lines[0],lines[6]));
            }
            result[row]=bestEdge;
        }
        return result;
    }
    // OCR crops are PNGs from FramePreparer; other default decoders (TIFF/BigTIFF...) are not
    // registered so ImageSharp <= 4.1.1 decoder advisories stay off the worker's input path.
    static readonly SixLabors.ImageSharp.Formats.DecoderOptions PngOnly = new()
        { MaxFrames=1, Configuration=new Configuration(new SixLabors.ImageSharp.Formats.Png.PngConfigurationModule()) };
    int HeroRole(int hero) => catalog.RootElement.GetProperty("roles").TryGetProperty(hero.ToString(),out var role) ? role.GetInt32() : 0;

    (string Text,string ConfidentText,TextWord[] Words) ReadText(byte[] png, bool player=false)
    {
        if(png.Length==0) return ("","",[]);
        var info=Image.Identify(PngOnly,png);
        if(info.Width>4096 || info.Height>512 || (long)info.Width*info.Height>1_048_576) throw new InvalidDataException("OCR crop too large");
        using var im=Image.Load<Rgb24>(PngOnly,png);
        if(player) RemovePlayerBadge(im);
        im.ProcessPixelRows(rows=> {
            for(int y=0;y<im.Height;y++) { var row=rows.GetRowSpan(y);
                for(int x=0;x<im.Width;x++) { var p=row[x]; int min=Math.Min(p.R,Math.Min(p.G,p.B)), max=Math.Max(p.R,Math.Max(p.G,p.B));
                    byte value=max-min<65 ? (byte)(255-min) : (byte)255;
                    row[x]=new Rgb24(value,value,value); }
            }
        });
        using var prepared=new Image<Rgb24>(im.Width+24,im.Height+24,new Rgb24(255,255,255));
        for(int y=0;y<im.Height;y++) for(int x=0;x<im.Width;x++) {
            int target=x+12+(player ? (int)Math.Round((y-im.Height/2.0)*.20) : 0);
            if(target>=0 && target<prepared.Width) prepared[target,y+12]=im[x,y];
        }
        if(prepared.Height<100) prepared.Mutate(c=>c.Resize(prepared.Width*2,prepared.Height*2));
        using var stream=new MemoryStream(); prepared.SaveAsPng(stream);
        using var pix=Pix.LoadFromMemory(stream.ToArray()); using var page=ocr.Process(pix,PageSegMode.SingleLine);
        var words=new List<TextWord>(); using var iterator=page.GetIterator(); iterator.Begin();
        do { words.Add(new(iterator.GetText(PageIteratorLevel.Word)??"",iterator.GetConfidence(PageIteratorLevel.Word))); }
        while(iterator.Next(PageIteratorLevel.Word));
        return (page.GetText().Trim(),string.Join(" ",words.Where(w=>w.OcrConfidence>=80).Select(w=>w.Text)),words.ToArray());
    }
    // The mastery badge is adjacent to (and taller than) the hero-name lettering.
    // Keeping it in SingleLine OCR merges the icon into names such as D.VA. Locate
    // its bright connected border rather than hard-coding a hero-dependent width.
    public static void RemovePlayerBadge(Image<Rgb24> im)
    {
        var seen=new bool[im.Width*im.Height]; var queue=new int[seen.Length];
        bool Ink(int x,int y) { var p=im[x,y]; return Math.Min(p.R,Math.Min(p.G,p.B))>=170 && Math.Max(p.R,Math.Max(p.G,p.B))-Math.Min(p.R,Math.Min(p.G,p.B))<65; }
        var components=new List<(int Left,int Right,int Top,int Bottom)>();
        for(int y=0;y<im.Height;y++) for(int x=0;x<im.Width;x++) {
            int start=y*im.Width+x; if(seen[start] || !Ink(x,y)) continue;
            int head=0,tail=1,left=x,right=x,top=y,bottom=y; queue[0]=start;seen[start]=true;
            while(head<tail) {
                int p=queue[head++],px=p%im.Width,py=p/im.Width;
                left=Math.Min(left,px);right=Math.Max(right,px);top=Math.Min(top,py);bottom=Math.Max(bottom,py);
                for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++) {
                    int nx=px+dx,ny=py+dy; if(nx<0 || ny<0 || nx>=im.Width || ny>=im.Height)continue;
                    int n=ny*im.Width+nx;if(seen[n] || !Ink(nx,ny))continue;seen[n]=true;queue[tail++]=n;
                }
            }
            if(tail>=3) components.Add((left,right,top,bottom));
        }
        // A badge can have disconnected upper/lower borders; join overlapping
        // horizontal extents, without joining across the space after the name.
        var groups=new List<(int Left,int Right,int Top,int Bottom)>();
        foreach(var c in components.OrderBy(c=>c.Left)) {
            if(groups.Count==0 || c.Left>groups[^1].Right+2) groups.Add(c);
            else { var g=groups[^1]; groups[^1]=(g.Left,Math.Max(g.Right,c.Right),Math.Min(g.Top,c.Top),Math.Max(g.Bottom,c.Bottom)); }
        }
        int cut=groups.Where(g=>g.Bottom-g.Top+1>im.Height*.68 && g.Left>im.Height*.5).Select(g=>g.Left).DefaultIfEmpty(im.Width).Min();
        if(cut<im.Width) im.Mutate(c=>c.Crop(new Rectangle(0,0,Math.Max(1,cut-3),im.Height)));
    }
    public int FindExactCatalog(string text,string section)
        => FindCatalogText(text, catalog.RootElement.GetProperty(section));

    public static int FindCatalogText(string text, JsonElement entries)
    {
        string normalized=text.ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
        if(Normalize(text).Length<3) return 0;
        var matches=entries.EnumerateObject()
            // NFD splits accents into combining marks, which .NET counts as word characters: skip them too.
            .Where(p=>Regex.IsMatch(normalized,"(?<![A-Z0-9])"+string.Join("[\\W_\\p{Mn}]*",Normalize(p.Value.GetString()!).Select(c=>Regex.Escape(c.ToString())))+"(?![A-Z0-9])"))
            .Select(p=>int.Parse(p.Name)).ToArray();
        return matches.Length==1 ? matches[0] : 0;
    }
    int FindExactPanelName(string text)
    {
        var normalized=Normalize(text);
        if(normalized.Length<3) return 0;
        var matches=catalog.RootElement.GetProperty("heroes").EnumerateObject()
            .Where(p=>Normalize(p.Value.GetString()!)==normalized).Select(p=>int.Parse(p.Name)).ToArray();
        return matches.Length==1 ? matches[0] : 0;
    }
    static string Normalize(string text) => Regex.Replace(text.ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD),"[^A-Z0-9]","");
    public void Dispose() { lock(gate) { if(disposed)return; disposed=true; recognizer.Dispose(); ocr.Dispose(); catalog.Dispose(); } }
}
