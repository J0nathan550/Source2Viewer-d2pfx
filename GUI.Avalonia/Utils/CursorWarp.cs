using System.Runtime.InteropServices;
using Avalonia;

namespace GUI.Utils;

/// <summary>
/// Moves the OS cursor, which mouse look uses to keep the cursor pinned while dragging so the look
/// does not stop at the screen edge. Avalonia has no API for this, so it is done per platform.
/// Platforms that cannot warp (such as Wayland) just report false and the look stops at the edge.
/// </summary>
static partial class CursorWarp
{
    private static nint x11Display;
    private static bool x11Unavailable;

    public static bool IsSupported => OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && GetX11Display() != 0);

    /// <param name="screenPosition">Target position in physical screen pixels, as returned by Visual.PointToScreen.</param>
    public static bool TryWarp(PixelPoint screenPosition)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return SetCursorPos(screenPosition.X, screenPosition.Y);
            }

            if (OperatingSystem.IsLinux() && GetX11Display() is var display and not 0)
            {
                _ = XWarpPointer(display, 0, XDefaultRootWindow(display), 0, 0, 0, 0, screenPosition.X, screenPosition.Y);
                _ = XFlush(display);
                return true;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            x11Unavailable = true;
        }

        return false;
    }

    private static nint GetX11Display()
    {
        if (x11Display != 0 || x11Unavailable)
        {
            return x11Display;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            x11Unavailable = true;
            return 0;
        }

        try
        {
            x11Display = XOpenDisplay(0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            x11Display = 0;
        }

        x11Unavailable = x11Display == 0;
        return x11Display;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XOpenDisplay(nint displayName);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XWarpPointer(nint display, nint sourceWindow, nint destinationWindow, int sourceX, int sourceY, uint sourceWidth, uint sourceHeight, int destinationX, int destinationY);

    [LibraryImport("libX11.so.6")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XFlush(nint display);
}
