// Synced from owmeta:tools/portrait-prototype/Worker/Recognizer.cs by tools/portrait-prototype/sync-hud-vision.ps1. Edit the owmeta source, not this copy.
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PortraitPrototype;

public sealed record Observation(int Hero, float Similarity, float Margin, int BestCandidate = 1, int RunnerUp = 1, string Reason = "accepted");

/// <summary>Portrait encoder and gallery matching on prepared crops. No network, history, app contracts, or legacy fallback.</summary>
public sealed class Recognizer : IDisposable
{
    const int Size = 48, CropBytes = Size * Size * 3;
    readonly string kind;
    readonly float threshold, margin;
    readonly float minGrayStd;
    readonly int dimension;
    readonly int[] identities;
    readonly int[] heroIds;
    public IReadOnlyList<int> HeroIds => Array.AsReadOnly(heroIds);
    readonly float[][] references;
    readonly InferenceSession? session;
    readonly Dictionary<int, DenseTensor<float>> inputBuffers = new();
    readonly Dictionary<int, float[][]> featureBuffers = new();
    readonly object gate = new();
    bool disposed;

    public Recognizer(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var m = manifest.RootElement;
        if (m.GetProperty("schema").GetInt32() != 1 || m.GetProperty("size").GetInt32() != Size)
            throw new InvalidDataException("Unsupported model pack");
        kind = m.GetProperty("kind").GetString()!;
        dimension = m.GetProperty("dimension").GetInt32();
        threshold = m.GetProperty("threshold").GetSingle(); margin = m.GetProperty("margin").GetSingle();
        minGrayStd = m.TryGetProperty("min_gray_std", out var quality) ? quality.GetSingle() : 0;
        if (kind is not ("template" or "neural") || dimension != (kind == "template" ? 1024 : 96)
            || !float.IsFinite(threshold) || threshold < 0 || threshold > 1.001f
            || !float.IsFinite(margin) || margin <= 0 || margin > 2
            || !float.IsFinite(minGrayStd) || minGrayStd < 0 || minGrayStd > 128)
            throw new InvalidDataException("Invalid model contract");
        foreach (var name in kind == "neural" ? new[] { "gallery.bin", "encoder.onnx" } : new[] { "gallery.bin" })
        {
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name))));
            if (!string.Equals(actual, m.GetProperty("checksums").GetProperty(name).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model checksum mismatch");
        }
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(directory, "gallery.bin")));
        if (reader.ReadUInt32() != 0x3147574f) throw new InvalidDataException("Invalid gallery header");
        var count = reader.ReadInt32();
        if (count < 2 || count > 10000 || reader.ReadInt32() != dimension || count != m.GetProperty("references").GetInt32())
            throw new InvalidDataException("Invalid gallery dimensions");
        identities = new int[count]; references = new float[count][];
        for (var r = 0; r < count; r++)
        {
            identities[r] = reader.ReadInt32();
            if (identities[r] < 1) throw new InvalidDataException("Invalid gallery identity");
            references[r] = new float[dimension];
            double norm = 0;
            for (var i = 0; i < dimension; i++)
            {
                var value = reader.ReadSingle();
                if (!float.IsFinite(value)) throw new InvalidDataException("Invalid gallery feature");
                references[r][i] = value; norm += value * value;
            }
            if (Math.Abs(norm - 1) > .01 && !(identities[r] == 1 && norm < 1e-6))
                throw new InvalidDataException("Unnormalized reference");
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length || identities.Distinct().Count() < 2)
            throw new InvalidDataException("Invalid gallery contents");
        // Compact score indices depend on gallery membership, not the largest enum ID.
        // The application validates membership against its checksummed catalog.
        heroIds = identities.Distinct().Order().ToArray();
        var indices = heroIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        identities = identities.Select(id => indices[id]).ToArray();
        if (kind == "neural")
        {
            using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
            session = new InferenceSession(Path.Combine(directory, "encoder.onnx"), options);
        }
        // Warm the same entry point used for actual requests.
        try { for (var n = 1; n <= 13; n++) Process(BlankPacket(n));
            var warm = BlankPacket(10); for (var i = 0; i < 150; i++) Process(warm); }
        catch { session?.Dispose(); throw; }
    }

    public static byte[] BlankPacket(int count)
    {
        if (count < 1 || count > 13) throw new ArgumentOutOfRangeException(nameof(count));
        var packet = new byte[16 + count * CropBytes];
        "OWP1"u8.CopyTo(packet); BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), Size);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), Size);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12), count); return packet;
    }

    public static int Validate(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 16 || !packet[..4].SequenceEqual("OWP1"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(packet[4..]) != Size
            || BinaryPrimitives.ReadInt32LittleEndian(packet[8..]) != Size)
            throw new InvalidDataException("Invalid prepared-pixel header");
        var n = BinaryPrimitives.ReadInt32LittleEndian(packet[12..]);
        if (n < 1 || n > 13 || packet.Length != 16 + n * CropBytes)
            throw new InvalidDataException("Invalid prepared-pixel length");
        return n;
    }

    public Observation[] Process(ReadOnlySpan<byte> packet)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var count = Validate(packet);
            if (!featureBuffers.TryGetValue(count, out var features))
            {
                features = Enumerable.Range(0, count).Select(_ => new float[dimension]).ToArray();
                featureBuffers.Add(count, features);
            }
            if (kind == "template")
                for (var i = 0; i < count; i++) Descriptor(packet.Slice(16 + i * CropBytes, CropBytes), features[i]);
            else
            {
                if (!inputBuffers.TryGetValue(count, out var tensor))
                {
                    tensor = new DenseTensor<float>(new[] { count, 3, Size, Size });
                    inputBuffers.Add(count, tensor);
                }
                var buffer = tensor.Buffer.Span;
                for (var n = 0; n < count; n++)
                    for (var y = 0; y < Size; y++)
                        for (var x = 0; x < Size; x++)
                            for (var c = 0; c < 3; c++)
                                buffer[(n * 3 + c) * Size * Size + y * Size + x] = packet[16 + n * CropBytes + (y * Size + x) * 3 + c] / 255f;
                using var output = session!.Run([NamedOnnxValue.CreateFromTensor("input", tensor)]);
                var values = output.Single().AsTensor<float>();
                if (values.Rank != 2 || values.Dimensions[0] != count || values.Dimensions[1] != dimension)
                    throw new InvalidDataException("Unexpected encoder output");
                for (var n = 0; n < count; n++)
                {
                    for (var i = 0; i < dimension; i++) features[n][i] = values.GetValue(n * dimension + i);
                }
            }
            var results = features.Select(Match).ToArray();
            for (var n = 0; n < count; n++)
                if (GrayStd(packet.Slice(16 + n * CropBytes, CropBytes)) < minGrayStd)
                    results[n] = new(1, 0, 0, Reason: "insufficient-pixel-evidence");
            return results;
        }
    }

    Observation Match(float[] feature)
    {
        if (feature.Any(v => !float.IsFinite(v)) || Dot(feature, feature) < .99f) return new(1, 0, 0, Reason: "invalid-feature");
        Span<float> scores = heroIds.Length <= 256 ? stackalloc float[heroIds.Length] : new float[heroIds.Length]; scores.Fill(-2);
        for (var r = 0; r < references.Length; r++)
            scores[identities[r]] = Math.Max(scores[identities[r]], Dot(feature, references[r]));
        var best = 0; var runnerUp = 0; float first = -2, second = -2;
        for (var h = 0; h < scores.Length; h++)
            if (scores[h] > first) { second = first; runnerUp = best; first = scores[h]; best = h; }
            else if (scores[h] > second) { second = scores[h]; runnerUp = h; }
        best = heroIds[best]; runnerUp = heroIds[runnerUp];
        var reason = first < threshold ? "below-similarity-threshold" : first-second < margin ? "ambiguous-identity" : best == 1 ? "unreadable-artwork" : best == 2 ? "explicitly-hidden" : "accepted";
        return new(first >= threshold && first - second >= margin ? best : 1, first, first - second, best, runnerUp, reason);
    }

    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Dimension mismatch");
        var vector = Vector<float>.Zero; int i = 0;
        for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
            vector += new Vector<float>(a.Slice(i)) * new Vector<float>(b.Slice(i));
        float sum = Vector.Sum(vector);
        for (; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    public static float[] Descriptor(ReadOnlySpan<byte> pixels)
    {
        var values = new float[1024];
        Descriptor(pixels, values);
        return values;
    }

    public static void Descriptor(ReadOnlySpan<byte> pixels, Span<float> values)
    {
        if (pixels.Length != CropBytes || values.Length != 1024) throw new ArgumentException("Expected 48x48 RGB8 and 1024 features");
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                var sx = (int)(4 + (x + .5) * 40 / 16); var sy = (int)(4 + (y + .5) * 40 / 16);
                var src = (sy * Size + sx) * 3; var dest = (y * 16 + x) * 3;
                for (var c = 0; c < 3; c++) values[dest + c] = pixels[src + c];
                values[768 + y * 16 + x] = (pixels[src] + pixels[src + 1] + pixels[src + 2]) / 3f;
            }
        Normalize(values[..768], MathF.Sqrt(.6f)); Normalize(values[768..], MathF.Sqrt(.4f));
    }

    public static float GrayStd(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != CropBytes) throw new ArgumentException("Expected 48x48 RGB8");
        double sum = 0, squares = 0;
        for (var y = 4; y < 44; y++)
            for (var x = 4; x < 44; x++)
            {
                var i = (y * Size + x) * 3;
                var gray = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3.0;
                sum += gray; squares += gray * gray;
            }
        return (float)Math.Sqrt(Math.Max(0, squares / 1600 - sum / 1600 * (sum / 1600)));
    }

    static void Normalize(Span<float> values, float weight)
    {
        float mean = 0; foreach (var v in values) mean += v; mean /= values.Length;
        for (var i = 0; i < values.Length; i++) values[i] -= mean;
        var norm = MathF.Max(MathF.Sqrt(Dot(values, values)), 1e-6f);
        for (var i = 0; i < values.Length; i++) values[i] *= weight / norm;
    }

    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; session?.Dispose(); } }
}
