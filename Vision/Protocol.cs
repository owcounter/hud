// Synced from owmeta:csharp/Owmeta.Vision.Protocol/Protocol.cs by tools/portrait-prototype/sync-hud-vision.ps1. Edit the owmeta source, not this copy.
namespace Owmeta.Vision.Protocol;

// Versioned prepared-frame and recognition-result types. Scores are similarities, never probabilities.
public sealed record Box(int X, int Y, int Width, int Height);
public sealed record PreparedFrame
{
    public int Schema { get; init; } = 1;
    public string PreparationVersion { get; init; } = "role-grid-v1";
    public string CaptureId { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public Box[] PortraitBoxes { get; init; } = [];
    public Box[] RoleBoxes { get; init; } = [];
    public byte[] Portraits { get; init; } = [];
    public byte[] RoleMasks { get; init; } = [];
    public byte[] MapPng { get; init; } = [];
    public byte[] PlayerNamePng { get; init; } = [];
    public byte[] PlayerPortrait { get; init; } = [];
    public Box? PlayerPortraitBox { get; init; }
    public byte[] SelectionEdges { get; init; } = [];
    public double PreparationMs { get; init; }
}

public sealed record SlotObservation(int Hero, float Similarity, float Margin, int BestCandidate, int RunnerUp, string Reason);
public sealed record RoleObservation(int Role, float Similarity, float Margin);
public sealed record TextWord(string Text, float OcrConfidence);
// Independent current-frame evidence, not a portrait similarity or probability.
public sealed record PlayerObservation(int Hero, int Row, string Source, float OcrConfidence, SlotObservation? Portrait = null);
public sealed record RecognitionResult
{
    public int Schema { get; init; } = 1;
    public string CaptureId { get; init; } = "";
    public string PackId { get; init; } = "";
    public string WorkerBuildId { get; init; } = "";
    public bool Valid { get; init; }
    public string Reason { get; init; } = "";
    public SlotObservation[] Slots { get; init; } = [];
    public RoleObservation[] Roles { get; init; } = [];
    public int PlayerHero { get; init; } = 1;
    public PlayerObservation? PlayerEvidence { get; init; }
    public SlotObservation? PlayerPortraitObservation { get; init; }
    public int Map { get; init; }
    public string PlayerText { get; init; } = "";
    public string MapText { get; init; } = "";
    public string OcrStatus { get; init; } = "";
    public TextWord[] MapWords { get; init; } = [];
    public TextWord[] PlayerWords { get; init; } = [];
    public float[] SelectionEdgeScores { get; init; } = [];
    public int SelectedRow { get; init; } = -1;
    public double IdentityMs { get; init; }
    public double WorkerMs { get; init; }
}
