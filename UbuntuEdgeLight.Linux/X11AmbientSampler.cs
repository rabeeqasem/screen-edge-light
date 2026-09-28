using System;
using System.Runtime.InteropServices;

namespace UbuntuEdgeLight;

/// <summary>
/// Samples the average color of a screen region using raw Xlib, so the light can tint
/// itself to match what's on screen (ambient / "match screen colors" mode).
///
/// Real X11 sessions only. Under Wayland the app still runs through XWayland, but the
/// XWayland root window doesn't contain other apps' pixels (reads come back black), so
/// <see cref="Available"/> is false there and callers degrade gracefully.
/// </summary>
public sealed class X11AmbientSampler : IDisposable
{
    private const string Lib = "libX11.so.6";
    private const int ZPixmap = 2;

    // Sample grid: this many horizontal strips across the region, one pixel every
    // `Step` along each. A handful of 1px-high reads instead of one full-screen grab
    // keeps each sample to a few KB even on 4K displays.
    private const int Rows = 12;
    private const int Step = 32;

    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] private static extern int XGetGeometry(IntPtr display, IntPtr drawable,
        out IntPtr root, out int x, out int y, out uint width, out uint height,
        out uint borderWidth, out uint depth);
    [DllImport(Lib)] private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable,
        int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport(Lib)] private static extern ulong XGetPixel(IntPtr image, int x, int y);

    // XDestroyImage is a C macro, not an exported symbol, so we invoke the
    // destroy_image function pointer stored inside the XImage struct instead.
    // On x86_64 the funcs sub-struct starts at offset 88; destroy_image is its
    // second pointer -> offset 96.
    private const int DestroyImageFnOffset = 96;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DestroyImageDelegate(IntPtr image);

    private IntPtr _display;
    private IntPtr _root;

    // Serializes Xlib calls on our connection and guards against Dispose() racing an
    // in-flight Sample() on a background thread.
    private readonly object _sync = new();

    public bool Available { get; private set; }

    public X11AmbientSampler()
    {
        // DestroyImageFnOffset and the pixel decode below assume the x86_64 Xlib ABI.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return;

        if (IsWaylandSession())
            return;

        try
        {
            _display = XOpenDisplay(null);
            if (_display != IntPtr.Zero)
            {
                _root = XDefaultRootWindow(_display);
                Available = _root != IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            Available = false;
        }
    }

    private static bool IsWaylandSession() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Averages a sparse grid of pixels inside the given region (root-window pixel
    /// coordinates). Runs on a background thread (XGetImage can block).
    /// Returns null if unavailable or on any failure.
    /// </summary>
    public (byte R, byte G, byte B)? Sample(int x, int y, int width, int height)
    {
        if (!Available)
            return null;

        lock (_sync)
        {
            if (_display == IntPtr.Zero)
                return null;
            return SampleLocked(x, y, width, height);
        }
    }

    private (byte R, byte G, byte B)? SampleLocked(int x, int y, int width, int height)
    {
        try
        {
            // Clamp to the root window's *current* size. Screen layout can change at any
            // time (resolution change, monitor unplugged), and reading outside the root
            // window is an X protocol error.
            if (XGetGeometry(_display, _root, out _, out _, out _, out uint rootW, out uint rootH, out _, out _) == 0)
                return null;

            int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
            int x1 = Math.Min((int)rootW, x + width), y1 = Math.Min((int)rootH, y + height);
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0)
                return null;

            long rs = 0, gs = 0, bs = 0, n = 0;

            for (int row = 0; row < Rows; row++)
            {
                int yy = y0 + (int)((row + 0.5) * h / Rows);
                IntPtr image = XGetImage(_display, _root, x0, yy, (uint)w, 1, ulong.MaxValue, ZPixmap);
                if (image == IntPtr.Zero)
                    continue;

                try
                {
                    for (int xx = Step / 2; xx < w; xx += Step)
                    {
                        ulong p = XGetPixel(image, xx, 0);
                        rs += (long)((p >> 16) & 0xFF);
                        gs += (long)((p >> 8) & 0xFF);
                        bs += (long)(p & 0xFF);
                        n++;
                    }
                }
                finally
                {
                    DestroyImage(image);
                }
            }

            if (n == 0)
                return null;

            return ((byte)(rs / n), (byte)(gs / n), (byte)(bs / n));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void DestroyImage(IntPtr image)
    {
        if (image == IntPtr.Zero)
            return;
        try
        {
            IntPtr fn = Marshal.ReadIntPtr(image, DestroyImageFnOffset);
            if (fn != IntPtr.Zero)
            {
                var destroy = Marshal.GetDelegateForFunctionPointer<DestroyImageDelegate>(fn);
                destroy(image);
            }
        }
        catch (Exception)
        {
            // Leaking one image is preferable to crashing; nothing else we can do.
        }
    }

    public void Dispose()
    {
        // Take the same lock as Sample() so we never close the display out from under
        // an in-flight background sample.
        lock (_sync)
        {
            if (_display != IntPtr.Zero)
            {
                try { XCloseDisplay(_display); } catch { /* ignore */ }
                _display = IntPtr.Zero;
                Available = false;
            }
        }
    }
}
