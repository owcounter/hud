using System.Drawing;
using System.Runtime.InteropServices;

namespace Owmeta.Services;

/// <summary>Positions the overlay exclusively in native screen pixels, never WPF DIPs.</summary>
public static class OverlayPlacement
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const int GwlExStyle = -20;
    private const int WsExTopmost = 0x00000008;
    private static readonly IntPtr HwndTopmost = new(-1);

    public static bool TryFollowWindow(IntPtr overlay, IntPtr target)
    {
        return TryGetClientBounds(target, out var bounds) && TrySetBounds(overlay, bounds);
    }

    public static bool TryGetClientBounds(IntPtr target, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (target == IntPtr.Zero || IsIconic(target) || !GetClientRect(target, out var client))
            return false;

        var topLeft = new Point(client.Left, client.Top);
        var bottomRight = new Point(client.Right, client.Bottom);
        if (!ClientToScreen(target, ref topLeft) || !ClientToScreen(target, ref bottomRight))
            return false;

        bounds = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static bool TrySetBounds(IntPtr overlay, Rectangle bounds)
    {
        if (overlay == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        if (!GetWindowRect(overlay, out var current))
            return false;
        if (current.Left == bounds.Left && current.Top == bounds.Top &&
            current.Right == bounds.Right && current.Bottom == bounds.Bottom)
        {
            return (GetWindowLong(overlay, GwlExStyle) & WsExTopmost) != 0 ||
                SetWindowPos(overlay, HwndTopmost, 0, 0, 0, 0,
                    SwpNoActivate | SwpNoMove | SwpNoSize);
        }

        // WPF updates its logical properties from the native window messages. Writing
        // Left/Top/Width/Height afterwards would scale these pixel values a second time.
        return SetWindowPos(overlay, HwndTopmost, bounds.X, bounds.Y,
            bounds.Width, bounds.Height, SwpNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
