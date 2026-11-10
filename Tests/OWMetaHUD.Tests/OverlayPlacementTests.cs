using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Owmeta.Services;

internal static class OverlayPlacementTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunOnUiThread(); }
            catch (Exception e) { failure = e; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Overlay placement regression", failure);
    }

    private static void RunOnUiThread()
    {
        // Invisible test windows: never move, activate, or change the real game/HUD.
        var overlay = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 2560, Height = 1440
        };
        var target = new Window { ShowInTaskbar = false, ShowActivated = false, Width = 800, Height = 600 };
        try
        {
            var overlayHandle = new WindowInteropHelper(overlay).EnsureHandle();
            var targetHandle = new WindowInteropHelper(target).EnsureHandle();
            if (GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(overlayHandle)) != 2)
                throw new Exception("Test executable must use the production per-monitor manifest");

            var cases = new List<Rectangle>
            {
                new(0, 0, 2560, 1440), new(-2560, -300, 2560, 1440),
                new(2560, 0, 2560, 1440), new(150, 200, 1280, 720)
            };
            cases.AddRange(System.Windows.Forms.Screen.AllScreens.Select(screen => screen.Bounds));
            foreach (var bounds in cases)
            {
                if (!OverlayPlacement.TrySetBounds(overlayHandle, bounds)) throw new Exception("Placement failed");
                FlushLayout(overlay);
                // A monitor transition may let WPF apply its DPI suggested rectangle.
                // Production queues the same correction after OnDpiChanged.
                if (!OverlayPlacement.TrySetBounds(overlayHandle, bounds)) throw new Exception("DPI realignment failed");
                FlushLayout(overlay);
                AssertBounds(overlayHandle, bounds);
                if (!OverlayPlacement.TrySetBounds(overlayHandle, bounds)) throw new Exception("Repeated placement failed");
                FlushLayout(overlay);
                AssertBounds(overlayHandle, bounds);
                Console.WriteLine($"PASS overlay pixels {bounds}, DPI={GetDpiForWindow(overlayHandle)}");

                // A decorated target must align to its client area, not include its title bar.
                if (!OverlayPlacement.TrySetBounds(targetHandle, bounds) ||
                    !OverlayPlacement.TryGetClientBounds(targetHandle, out var client) ||
                    !OverlayPlacement.TryFollowWindow(overlayHandle, targetHandle))
                    throw new Exception("Client-area following failed");
                FlushLayout(overlay);
                OverlayPlacement.TryFollowWindow(overlayHandle, targetHandle);
                FlushLayout(overlay);
                AssertBounds(overlayHandle, client);
            }

            var before = ReadBounds(overlayHandle);
            if (!SetWindowPos(overlayHandle, new IntPtr(-2), 0, 0, 0, 0, 0x0013))
                throw new Exception("Could not remove test window topmost status");
            FlushLayout(overlay);
            if ((GetWindowLong(overlayHandle, -20) & 8) != 0)
                throw new Exception("Test setup did not remove topmost status");
            if (!OverlayPlacement.TrySetBounds(overlayHandle, before) ||
                (GetWindowLong(overlayHandle, -20) & 8) == 0)
                throw new Exception("Matching bounds did not restore topmost status");
            AssertBounds(overlayHandle, before);
            if (OverlayPlacement.TryFollowWindow(overlayHandle, IntPtr.Zero) ||
                OverlayPlacement.TrySetBounds(overlayHandle, Rectangle.Empty) ||
                OverlayPlacement.TrySetBounds(IntPtr.Zero, cases[0]))
                throw new Exception("Invalid target/bounds accepted");
            AssertBounds(overlayHandle, before);
            if (IsWindowVisible(overlayHandle) || IsWindowVisible(targetHandle))
                throw new Exception("Placement unexpectedly showed a hidden window");
            Console.WriteLine("PASS client-area alignment, repeated placement, topmost restoration, invalid targets, hidden-window preservation");
        }
        finally { target.Close(); overlay.Close(); }
    }

    private static void FlushLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static Rectangle ReadBounds(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect)) throw new Exception("Could not read test window bounds");
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static void AssertBounds(IntPtr window, Rectangle expected)
    {
        var actual = ReadBounds(window);
        if (actual != expected) throw new Exception($"Expected physical bounds {expected}; got {actual}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
}
