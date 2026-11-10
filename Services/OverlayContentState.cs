namespace Owmeta.Services;

// Desired panel state is independent of temporary screenshot/presence suppression.
public sealed class OverlayContentState
{
    public int Panel { get; private set; }
    public bool Capturing { get; set; }
    public bool GameAvailable { get; set; }
    public bool ShowSwap => GameAvailable && !Capturing && Panel == 1;
    public bool ShowComposition => GameAvailable && !Capturing && Panel == 2;
    public void Toggle(int panel) => Panel = Panel == panel ? 0 : panel;
}
