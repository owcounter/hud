namespace Owmeta.Services;

public static class ScreenshotCaptureGuard
{
    public static void Check(CancellationToken cancellation, Func<bool> keyIsHeld)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!keyIsHeld()) throw new OperationCanceledException("Screenshot key released before pixels were captured");
    }
}
