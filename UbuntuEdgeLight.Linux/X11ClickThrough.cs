using System;
using System.Runtime.InteropServices;

namespace UbuntuEdgeLight;

/// <summary>
/// Makes an X11 window click-through (input-transparent) by giving it an empty
/// input shape via the XShape extension. Pointer/click events then pass to whatever
/// is underneath, so the full-screen overlay never blocks the desktop and never needs
/// to hide itself. X11 only; fails quietly elsewhere.
/// </summary>
public static class X11ClickThrough
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";

    private const int ShapeInput = 2; // shape kind: input region
    private const int ShapeSet = 0;   // shape op: replace

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern int XFlush(IntPtr display);

    [DllImport(LibXext)]
    private static extern void XShapeCombineRectangles(
        IntPtr display, IntPtr window, int destKind,
        int xOff, int yOff, IntPtr rectangles, int nRects, int op, int ordering);

    /// <summary>Apply an empty input shape to the given X11 window id.</summary>
    public static void Apply(IntPtr xid)
    {
        if (xid == IntPtr.Zero)
            return;

        IntPtr display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                return;

            // An empty rectangle list (NULL, 0) for the input shape means no part of the
            // window accepts pointer input -> fully click-through.
            XShapeCombineRectangles(display, xid, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, 0);
            XFlush(display);
        }
        catch (Exception)
        {
            // XShape unavailable (e.g. non-X11) -> overlay stays interactive; non-fatal.
        }
        finally
        {
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }
    }
}
