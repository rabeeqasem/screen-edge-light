using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UbuntuEdgeLight;

class Program
{
    /// <summary>
    /// Unix-domain-socket IPC path. Must live in a directory that is shared across
    /// separate launches of the app. Inside a snap, /tmp is private per-launch, so we
    /// use $SNAP_USER_COMMON (persistent, shared) and fall back to the runtime dir / temp
    /// when running unconfined.
    /// </summary>
    private static string IpcSocketPath()
    {
        var dir = Environment.GetEnvironmentVariable("SNAP_USER_COMMON")
                  ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                  ?? Path.GetTempPath();
        return Path.Combine(dir, "screen-edge-light.sock");
    }

    public static void SendIpcMessage(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var endpoint = new UnixDomainSocketEndPoint(IpcSocketPath());

        // Retry briefly: the primary instance holds the single-instance lock slightly
        // before its IPC server finishes binding, so the first connect can race-fail.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                sock.Connect(endpoint);
                sock.Send(bytes);
                return;
            }
            catch
            {
                Thread.Sleep(150);
            }
        }
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--toggle")
        {
            SendIpcMessage("TOGGLE");
            return;
        }

        // Use a FileStream lock for single instance check (more robust on Linux/Snap)
        var lockFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UbuntuEdgeLight",
            "instance.lock");

        FileStream lockFile;
        try
        {
            var dir = Path.GetDirectoryName(lockFilePath);
            if (dir != null) Directory.CreateDirectory(dir);

            // Try to open the file with exclusive access. If it fails, the app is running.
            lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Already running: a second launch (e.g. clicking the app icon again)
            // re-opens the control window instead.
            SendIpcMessage("SHOW_CONTROLS");
            return;
        }

        // Keep the lock held for the lifetime of the app. Only the lock acquisition above
        // is treated as "already running": an IOException while the app is running must
        // surface as a real error, not be mistaken for a second instance and exit silently.
        using (lockFile)
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
    }

    public static void StartIpcServer(Window mainWindow)
    {
        var path = IpcSocketPath();
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }

        Task.Run(async () =>
        {
            try
            {
                using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                server.Bind(new UnixDomainSocketEndPoint(path));
                server.Listen(16);

                while (true)
                {
                    try
                    {
                        using var client = await server.AcceptAsync();

                        // Unix sockets are byte streams with no framing: read until the
                        // sender closes so a split command isn't mis-parsed.
                        var sb = new StringBuilder();
                        var buffer = new byte[256];
                        int n;
                        while ((n = client.Receive(buffer)) > 0)
                        {
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                            if (sb.Length > 4096) break;
                        }
                        var message = sb.ToString().Trim();

                        if (message == "TOGGLE" && mainWindow is MainWindow light)
                        {
                            Dispatcher.UIThread.Post(() => light.ToggleLight());
                        }
                        else if (message == "SHOW_CONTROLS" && mainWindow is MainWindow main)
                        {
                            Dispatcher.UIThread.Post(() => main.ShowControls());
                        }
                    }
                    catch
                    {
                        // Keep the server alive on transient errors.
                        await Task.Delay(200);
                    }
                }
            }
            catch
            {
                // Couldn't bind the socket; IPC simply unavailable this session.
            }
        });
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
