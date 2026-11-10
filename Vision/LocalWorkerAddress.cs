// Synced from owmeta:csharp/Owmeta.Vision.Protocol/LocalWorkerAddress.cs by tools/portrait-prototype/sync-hud-vision.ps1. Edit the owmeta source, not this copy.
namespace Owmeta.Vision.Protocol;

/// <summary>Port-only override for isolated local tests; never permits a remote listener.</summary>
public static class LocalWorkerAddress
{
    public static Uri FromPort(string? value)
    {
        if (value is null) return new Uri("http://127.0.0.1:17865");
        if (!int.TryParse(value, out var port) || port is < 1024 or > 65535)
            throw new ArgumentException("OWMETA_VISION_PORT must be an integer from 1024 to 65535");
        return new Uri($"http://127.0.0.1:{port}");
    }

    public static Uri Current => FromPort(Environment.GetEnvironmentVariable("OWMETA_VISION_PORT"));
}
