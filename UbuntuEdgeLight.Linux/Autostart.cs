using System;
using System.IO;

namespace UbuntuEdgeLight;

/// <summary>
/// "Start on login" via an XDG autostart entry.
///
/// Inside the snap, $HOME is $SNAP_USER_DATA, so this writes
/// $SNAP_USER_DATA/.config/autostart/screen-edge-light.desktop. That is exactly where
/// snapd's user-session agent looks for the file named by the app's `autostart:` key in
/// snapcraft.yaml, and it then launches the app at login. Unconfined (e.g. `dotnet run`),
/// it lands in the normal ~/.config/autostart and points at the running executable.
/// </summary>
public static class Autostart
{
    private const string FileName = "screen-edge-light.desktop";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart",
        FileName);

    public static bool IsEnabled
    {
        get
        {
            try { return File.Exists(FilePath); }
            catch { return false; }
        }
    }

    /// <summary>Returns true if the change was applied.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                return true;
            }

            // snapd ignores the Exec path (it always runs the snap's own app), but it
            // must be present; outside a snap it has to be the real executable.
            string exec = Environment.GetEnvironmentVariable("SNAP") != null
                ? "screen-edge-light"
                : $"\"{Environment.ProcessPath}\"";

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Screen Edge Light\n" +
                "Comment=Ring light for your monitor\n" +
                $"Exec={exec}\n" +
                "Terminal=false\n" +
                "X-GNOME-Autostart-enabled=true\n");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update autostart: {ex.Message}");
            return false;
        }
    }
}
