using System;
using System.Runtime.InteropServices;

namespace UbuntuEdgeLight;

/// <summary>
/// Samples the average color near the screen edges using raw Xlib, so the studio
/// flood can tint itself to match what's on screen (ambient / "screen match" mode).
///
/// X11 only. Under a pure Wayland session XOpenDisplay returns null and
/// <see cref="Available"/> stays false, so callers degrade gracefully.
/// </summary>
public sealed class X11AmbientSampler : IDisposable
{
    private const string Lib = "libX11.so.6";
    private const int ZPixmap = 2;

    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] private static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport(Lib)] private static extern int XDisplayHeight(IntPtr display, int screen);
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
    private int _width;
    private int _height;

    // Serializes Xlib calls on our connection and guards against Dispose() racing an
    // in-flight Sample() on a background thread.
    private readonly object _sync = new();

    public bool Available { get; private set; }

    public X11AmbientSampler()
    {
        // DestroyImageFnOffset and the pixel decode below assume the x86_64 Xlib ABI.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return;

        try
        {
            _display = XOpenDisplay(null);
            if (_display != IntPtr.Zero)
            {
                _root = XDefaultRootWindow(_display);
                _width = XDisplayWidth(_display, 0);
                _height = XDisplayHeight(_display, 0);
                Available = _width > 0 && _height > 0;
            }
        }
        catch (DllNotFoundException)
        {
            Available = false;
        }
        catch (Exception)
        {
            Available = false;
        }
    }

    /// <summary>
    /// Grabs the current screen contents and averages a sparse set of pixels within
    /// a band along the edges. Runs on a background thread (XGetImage can block).
    /// Returns null if unavailable or on any failure.
    /// </summary>
    public (byte R, byte G, byte B)? Sample()
    {
        if (!Available)
            return null;

        lock (_sync)
        {
            if (_display == IntPtr.Zero)
                return null;
            return SampleLocked();
        }
    }

    private (byte R, byte G, byte B)? SampleLocked()
    {
        IntPtr image = IntPtr.Zero;
        try
        {
            image = XGetImage(_display, _root, 0, 0, (uint)_width, (uint)_height, ulong.MaxValue, ZPixmap);
            if (image == IntPtr.Zero)
                return null;

            long rs = 0, gs = 0, bs = 0, n = 0;
            int band = Math.Min(220, Math.Min(_width, _height) / 3);
            const int step = 48;

            for (int y = 0; y < _height; y += step)
            {
                for (int x = 0; x < _width; x += step)
                {
                    bool nearEdge = x < band || x > _width - band || y < band || y > _height - band;
                    if (!nearEdge)
                        continue;

                    ulong p = XGetPixel(image, x, y);
                    rs += (long)((p >> 16) & 0xFF);
                    gs += (long)((p >> 8) & 0xFF);
                    bs += (long)(p & 0xFF);
                    n++;
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
        finally
        {
            DestroyImage(image);
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
