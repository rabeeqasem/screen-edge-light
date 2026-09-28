using System;
using System.IO;
using System.Text.Json;

namespace UbuntuEdgeLight;

/// <summary>
/// Built-in Studio Light presets. Custom means the user is tuning sliders by hand.
/// </summary>
public enum LightMode
{
    Custom,
    SoftKey,
    WarmStudio,
    Daylight,
    Cool,
    Candle
}

/// <summary>
/// How the light is rendered: a thin decorative border, or a wide soft flood
/// that actually casts light onto the user (studio-light behavior).
/// </summary>
public enum IlluminationMode
{
    ThinBorder,
    StudioFlood
}

/// <summary>
/// Application settings that persist across sessions
/// </summary>
public class AppSettings
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UbuntuEdgeLight",
        "settings.json");

    // First-run defaults match the "Soft Key" preset: a flattering, face-lighting studio
    // flood is the best first impression for a video-call light.
    public double Opacity { get; set; } = 0.85;
    public double ColorTemperature { get; set; } = 0.45;
    public double BorderThickness { get; set; } = 14.0;
    public double Margin { get; set; } = 0.0;
    public double CornerRadius { get; set; } = 0.0;

    // --- Studio Light ---
    /// <summary>Thin border vs. wide studio flood.</summary>
    public IlluminationMode Illumination { get; set; } = IlluminationMode.StudioFlood;

    /// <summary>Currently selected preset (Custom when sliders are hand-tuned).</summary>
    public LightMode Preset { get; set; } = LightMode.SoftKey;

    /// <summary>Width in pixels of the studio flood gradient (how far light spreads inward).</summary>
    public double Spread { get; set; } = 180.0;

    /// <summary>Automatically warm/dim at night and cool/brighten midday.</summary>
    public bool AdaptiveTimeOfDay { get; set; } = false;

    /// <summary>Subtly tint the light to match the average color on screen (X11 only).</summary>
    public bool AdaptiveAmbient { get; set; } = false;

    // Control Window Position
    public int? ControlWindowX { get; set; }
    public int? ControlWindowY { get; set; }

    /// <summary>
    /// Clamp every numeric field into its valid range. Applied after loading so a
    /// hand-edited or partially corrupt settings.json can't push out-of-range values
    /// (e.g. Opacity=50) straight into the renderer.
    /// </summary>
    private void Validate()
    {
        Opacity = Math.Clamp(Opacity, 0.2, 1.0);
        ColorTemperature = Math.Clamp(ColorTemperature, 0.0, 1.0);
        BorderThickness = Math.Clamp(BorderThickness, 1.0, 50.0);
        Margin = Math.Max(0.0, Margin);
        CornerRadius = Math.Max(0.0, CornerRadius);
        Spread = Math.Clamp(Spread, 20.0, 600.0);
        if (!Enum.IsDefined(Illumination)) Illumination = IlluminationMode.ThinBorder;
        if (!Enum.IsDefined(Preset)) Preset = LightMode.Custom;
    }

    /// <summary>
    /// Load settings from disk
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var settings = JsonSerializer.Deserialize<AppSettings>(json, options);

                if (settings != null)
                {
                    settings.Validate();
                    return settings;
                }
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse settings file: {ex.Message}");
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                }
            }
            catch { /* Ignore deletion errors */ }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>
    /// Save settings to disk
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}
