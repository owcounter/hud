// Synced from owmeta:csharp/Owmeta.Vision.Preparation/FramePreparer.cs by tools/portrait-prototype/sync-hud-vision.ps1. Edit the owmeta source, not this copy.
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO; // Explicit: WPF implicit usings omit System.IO.
using Owmeta.Vision.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Owmeta.Vision.Preparation;

/// <summary>Geometry and pixels only: crops the scoreboard for <c>VisionEngine</c>, which does all recognition.</summary>
public static class FramePreparer
{
    public const int MaximumImageBytes = 24 * 1024 * 1024;
    // Screenshots are PNG captures (JPEG in dev/replay folders). Not registering the other
    // default decoders (TIFF/BigTIFF...) keeps ImageSharp <= 4.1.1 decoder advisories off this path.
    static readonly SixLabors.ImageSharp.Formats.DecoderOptions Decoding = new()
    {
        MaxFrames = 1,
        Configuration = new Configuration(
            new SixLabors.ImageSharp.Formats.Png.PngConfigurationModule(),
            new SixLabors.ImageSharp.Formats.Jpeg.JpegConfigurationModule())
    };
    sealed record Glyph(double X, double Y, int Left, int Top, int Width, int Height);
    sealed record Candidate(double Error, double Spacing, double X, double[] Centers, Glyph[] Glyphs);

    public static PreparedFrame Prepare(byte[] encoded, string captureId)
    {
        var timer = Stopwatch.StartNew();
        if (encoded.Length is 0 or > MaximumImageBytes) throw new InvalidDataException("Screenshot size is unsupported");
        var info = Image.Identify(Decoding, encoded);
        if (info.Width < 320 || info.Height < 240 || info.Width > 8192 || info.Height > 8192
            || (long)info.Width * info.Height > 33_554_432 || info.Width / (double)info.Height is < 1.2 or > 4)
            throw new InvalidDataException("Screenshot dimensions are unsupported");
        using var source = Image.Load<Rgb24>(Decoding, encoded);
        using var small = source.Clone(c => c.Resize((int)Math.Round(source.Width * 720.0 / source.Height), 720, KnownResamplers.Box));
        int w = small.Width, h = small.Height;
        var mask = new byte[w * h]; int peak = 0;
        small.ProcessPixelRows(rows => {
            for (int y = 0; y < h; y++) foreach (var p in rows.GetRowSpan(y)) peak = Math.Max(peak, Math.Max(p.R, Math.Max(p.G, p.B)));
            for (int y = 86; y < 655; y++) {
                var row = rows.GetRowSpan(y);
                for (int x = (int)Math.Round(w * .06); x < (int)Math.Round(w * .47); x++) {
                    var p = row[x]; int max = Math.Max(p.R, Math.Max(p.G, p.B)), min = Math.Min(p.R, Math.Min(p.G, p.B));
                    if (min > Math.Max(30, peak * .8) && max - min < Math.Max(5, peak * .14)) mask[y * w + x] = 1;
                }
            }
        });
        // The same horizontal close as the offline locator, with no team-color anchor.
        var dilated = new byte[mask.Length]; var closed = new byte[mask.Length];
        for (int y = 0; y < h; y++) for (int x = 1; x < w - 1; x++) {
            int i = y * w + x; dilated[i] = (byte)(mask[i-1] | mask[i] | mask[i+1]);
        }
        for (int y = 0; y < h; y++) for (int x = 1; x < w - 1; x++) {
            int i = y * w + x; closed[i] = (byte)(dilated[i-1] & dilated[i] & dilated[i+1]);
        }
        var glyphs = Components(closed, w, h);
        var candidates = new List<Candidate>();
        foreach (var glyph in glyphs) {
            var column = glyphs.Where(g => Math.Abs(g.X - glyph.X) < 3).OrderBy(g => g.Y).DistinctBy(g => g.Y).ToArray();
            if (column.Length is not (10 or 12)) continue;
            int n = column.Length / 2;
            var gaps = column.Zip(column.Skip(1), (a,b) => b.Y-a.Y).ToArray();
            var inner = gaps.Where((_, i) => i != n-1).ToArray();
            var sorted = inner.Order().ToArray(); double spacing = (sorted[(sorted.Length-1)/2] + sorted[sorted.Length/2]) / 2;
            if (spacing is <= 30 or >= 65 || gaps[n-1] < spacing * 1.5 || inner.Max(v => Math.Abs(v-spacing)) > 4) continue;
            double[] centers = [column.Take(n).Select((g,r) => g.Y-r*spacing).Average(), column.Skip(n).Select((g,r) => g.Y-r*spacing).Average()];
            candidates.Add(new(Math.Sqrt(inner.Average(v => Math.Pow(v-inner.Average(),2))), spacing, glyph.X, centers, column));
        }
        if (candidates.Count == 0) throw new InvalidDataException("No complete scoreboard grid found");
        var best = candidates.MinBy(c => c.Error)!;
        if (best.Glyphs.Length != 10) throw new InvalidDataException("Only 5v5 role-queue scoreboards are supported");
        if (candidates.Any(c => Math.Abs(c.X-best.X) > 6 || c.Glyphs.Length != 10)) throw new InvalidDataException("Ambiguous scoreboard grid");
        double scale = source.Height / 720.0, faceX = best.X + best.Spacing * .22;
        Box ConvertBox(double x, double y, double width, double height) {
            int left = (int)Math.Round(x*scale), top = (int)Math.Round(y*scale);
            return new(left, top, (int)Math.Round((x+width)*scale)-left, (int)Math.Round((y+height)*scale)-top);
        }
        var boxes = Enumerable.Range(0,10).Select(i => ConvertBox(faceX, best.Centers[i/5]+(i%5-.5)*best.Spacing, best.Spacing,best.Spacing)).ToArray();
        if (boxes.Any(b => !InBounds(b,source.Width,source.Height))) throw new InvalidDataException("Portrait grid extends outside screenshot");
        var packet = new byte[16 + 10*48*48*3]; "OWP1"u8.CopyTo(packet);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4),48); BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8),48); BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12),10);
        for (int i = 0; i < boxes.Length; i++) {
            var b = boxes[i]; using var crop = source.Clone(c => c.Crop(new Rectangle(b.X,b.Y,b.Width,b.Height)).Resize(48,48,KnownResamplers.Bicubic));
            crop.CopyPixelDataTo(packet.AsSpan(16+i*48*48*3,48*48*3));
        }
        var roleMasks = new byte[10*12*16];
        for (int i = 0; i < 10; i++) {
            var g = best.Glyphs[i];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 12; x++)
                roleMasks[i*192+y*12+x] = mask[(g.Top + Math.Min(g.Height-1,(int)((y+.5)*g.Height/16))) * w + g.Left + Math.Min(g.Width-1,(int)((x+.5)*g.Width/12))];
        }
        byte[] Png(Box b) {
            if (!InBounds(b,source.Width,source.Height)) return [];
            using var crop = source.Clone(c => c.Crop(new Rectangle(b.X,b.Y,b.Width,b.Height)));
            using var stream = new MemoryStream(); crop.SaveAsPng(stream); return stream.ToArray();
        }
        // Thin strips around friendly row boundaries. The engine decides whether an
        // explicit selection outline is present; preparation merely samples the pixels.
        var selectionEdges = new byte[5*2*7*160*3];
        for(int row=0;row<5;row++) for(int edge=0;edge<2;edge++) for(int dy=0;dy<7;dy++) for(int dx=0;dx<160;dx++) {
            int x=(int)Math.Round((faceX+best.Spacing*(1.2+dx/159.0*10))*scale);
            int y=(int)Math.Round((best.Centers[0]+(row+edge-.5)*best.Spacing+(dy-3)*.5)*scale);
            if(x<0 || x>=source.Width || y<0 || y>=source.Height) continue;
            var p=source[x,y]; int offset=(((row*2+edge)*7+dy)*160+dx)*3;
            selectionEdges[offset]=p.R; selectionEdges[offset+1]=p.G; selectionEdges[offset+2]=p.B;
        }
        var panelBox=ConvertBox(w-faceX-best.Spacing*6.2,best.Centers[0]-best.Spacing,best.Spacing*2.6,best.Spacing*2.6);
        byte[] panelPacket=[];
        if(InBounds(panelBox,source.Width,source.Height)) {
            panelPacket=new byte[16+48*48*3]; packet.AsSpan(0,16).CopyTo(panelPacket);
            BinaryPrimitives.WriteInt32LittleEndian(panelPacket.AsSpan(12),1);
            using var crop=source.Clone(c=>c.Crop(new Rectangle(panelBox.X,panelBox.Y,panelBox.Width,panelBox.Height)).Resize(48,48,KnownResamplers.Bicubic));
            crop.CopyPixelDataTo(panelPacket.AsSpan(16));
        }
        return new PreparedFrame {
            CaptureId = captureId, Width = source.Width, Height = source.Height, PortraitBoxes = boxes,
            RoleBoxes = best.Glyphs.Select(g => ConvertBox(g.Left,g.Top,g.Width,g.Height)).ToArray(),
            Portraits = packet, RoleMasks = roleMasks,
            SelectionEdges = selectionEdges,
            PlayerPortrait=panelPacket, PlayerPortraitBox=panelPacket.Length>0 ? panelBox : null,
            MapPng = Png(ConvertBox(w*.55, 12, w*.43, 32)),
            // Relative to the fitted scoreboard; absence or a changed panel produces UNKNOWN in the engine.
            PlayerNamePng = Png(ConvertBox(w-faceX-best.Spacing*5.85, best.Centers[0]+best.Spacing*2.1, best.Spacing*6.4, best.Spacing*.8)),
            PreparationMs = timer.Elapsed.TotalMilliseconds
        };
    }

    public static bool InBounds(Box b, int width, int height) => b.X >= 0 && b.Y >= 0 && b.Width > 0 && b.Height > 0 && (long)b.X+b.Width <= width && (long)b.Y+b.Height <= height;

    static List<Glyph> Components(byte[] mask, int w, int h)
    {
        var seen = new bool[mask.Length]; var queue = new int[mask.Length]; var result = new List<Glyph>();
        for (int start = 0; start < mask.Length; start++) {
            if (mask[start] == 0 || seen[start]) continue;
            int head = 0, tail = 1, left = w, right = 0, top = h, bottom = 0; queue[0] = start; seen[start] = true;
            while (head < tail) {
                int current = queue[head++], x = current%w, y = current/w;
                left = Math.Min(left,x); right = Math.Max(right,x); top = Math.Min(top,y); bottom = Math.Max(bottom,y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
                    int nx=x+dx, ny=y+dy; if (nx<0 || nx>=w || ny<0 || ny>=h) continue;
                    int next=ny*w+nx; if (seen[next] || mask[next]==0) continue; seen[next]=true; queue[tail++]=next;
                }
            }
            int gw=right-left+1, gh=bottom-top+1;
            if (gw is >=5 and <=20 && gh is >=7 and <=23 && tail >=35) result.Add(new(left+gw/2.0,top+gh/2.0,left,top,gw,gh));
        }
        return result;
    }
}
